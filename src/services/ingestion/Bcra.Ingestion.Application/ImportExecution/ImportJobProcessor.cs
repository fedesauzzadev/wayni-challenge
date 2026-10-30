using System.Data.Common;
using System.Diagnostics;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Application.ImportExecution;

public enum ImportProcessingOutcome
{
    NothingToClaim,
    Completed,
    AbortedByConnectionLoss,
    Failed
}

public sealed record ImportProcessingResult(ImportProcessingOutcome Outcome, Guid? JobId)
{
    public static ImportProcessingResult Nothing { get; } = new(ImportProcessingOutcome.NothingToClaim, null);
}

/// <summary>
/// Orquesta la ejecución de un import bajo lease: reclamo SKIP LOCKED, almacenamiento por import,
/// carga de referencias, carga por lotes con checkpoint (<c>bytes_committed</c>), agregación SQL,
/// índices pre-swap, swap atómico de <c>active_import</c> y limpieza por DROP del import anterior.
/// Si se pierde la conexión que sostiene el trabajo, aborta sin confirmar nada más: el lease
/// queda sin heartbeat y otro worker puede reclamar el job al vencer (ADR-005).
/// Al completar publica <see cref="ImportCompletedEvent"/> de forma best-effort: su fallo se
/// loguea pero no falla el import. Cada fase y el resultado se miden con <c>ImportPipelineMeter</c>.
/// </summary>
public sealed partial class ImportJobProcessor(
    IImportJobStore jobStore,
    IImportStorageManager storageManager,
    IReferenceTableStorage referenceStorage,
    IFileSourceFactory fileSourceFactory,
    StagingCopyLoader stagingLoader,
    ISituacionAggregationStrategy situacionStrategy,
    IImportEventPublisher eventPublisher,
    TimeProvider clock,
    IOptions<IngestionOptions> options,
    ImportPipelineMeter pipelineMeter,
    ILogger<ImportJobProcessor> logger)
{
    public async Task<ImportProcessingResult> TryProcessNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leaseTimeout = TimeSpan.FromSeconds(options.Value.LeaseTimeoutSeconds);

        var job = await jobStore.ClaimNextAsync(workerId, now, now - leaseTimeout, cancellationToken);
        if (job is null)
            return ImportProcessingResult.Nothing;

        try
        {
            var totalStart = Stopwatch.GetTimestamp();

            await storageManager.CreateImportStorageAsync(job.Id, cancellationToken);

            if (job.Status == ImportJobStatus.Loading)
            {
                await MeasurePhaseAsync(ImportPipelineMeter.PhaseStaging, async ct =>
                {
                    await LoadReferenceTablesAsync(job, ct);

                    await using var source = fileSourceFactory.Create(job.Source);
                    await stagingLoader.LoadAsync(job, source, workerId, ct);
                }, cancellationToken);

                job.MarkAggregating();
                await jobStore.SaveAsync(job, cancellationToken);
            }

            await jobStore.RenewLeaseAsync(job.Id, workerId, clock.GetUtcNow(), cancellationToken);

            await MeasurePhaseAsync(ImportPipelineMeter.PhaseAggregation,
                ct => storageManager.AggregateImportAsync(job.Id, situacionStrategy.BuildAggregationExpression(), ct), cancellationToken);

            await MeasurePhaseAsync(ImportPipelineMeter.PhaseIndexes,
                ct => storageManager.CreateImportIndexesAsync(job.Id, ct), cancellationToken);

            var previousImportId = await MeasurePhaseAsync(ImportPipelineMeter.PhaseSwap,
                ct => storageManager.SwapActiveImportAsync(job.Id, ct), cancellationToken);

            job.MarkCompleted(clock.GetUtcNow());
            await jobStore.SaveAsync(job, cancellationToken);

            await PublishCompletedEventAsync(job, cancellationToken);

            if (previousImportId is { } previous && previous != job.Id)
                await storageManager.DropImportStorageAsync(previous, cancellationToken);

            pipelineMeter.RecordPhaseDuration(ImportPipelineMeter.PhaseTotal, Stopwatch.GetElapsedTime(totalStart));
            pipelineMeter.AddJobOutcome("completed");

            return new ImportProcessingResult(ImportProcessingOutcome.Completed, job.Id);
        }
        catch (SourceChangedException ex)
        {
            var result = await MarkJobFailedAsync(job, cancellationToken);
            LogSourceChanged(job.Id, ex);
            return result;
        }
        catch (FileNotFoundException ex)
        {
            var result = await MarkJobFailedAsync(job, cancellationToken);
            LogSourceNotFound(job.Id, ex);
            return result;
        }
        catch (DbException ex)
        {
            LogConnectionLost(job.Id, ex);
            return new ImportProcessingResult(ImportProcessingOutcome.AbortedByConnectionLoss, job.Id);
        }
    }

    private async Task<ImportProcessingResult> MarkJobFailedAsync(ImportJob job, CancellationToken cancellationToken)
    {
        job.MarkFailed(clock.GetUtcNow());
        await jobStore.SaveAsync(job, cancellationToken);
        pipelineMeter.AddJobOutcome("failed");
        return new ImportProcessingResult(ImportProcessingOutcome.Failed, job.Id);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database failure assumed to be connection loss while processing job {JobId}; aborting so another worker can reclaim it when the lease expires")]
    private partial void LogConnectionLost(Guid jobId, Exception exception);

    private async Task<T> MeasurePhaseAsync<T>(string phase, Func<CancellationToken, Task<T>> phaseAction, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var result = await phaseAction(cancellationToken);
        pipelineMeter.RecordPhaseDuration(phase, Stopwatch.GetElapsedTime(start));
        return result;
    }

    private async Task MeasurePhaseAsync(string phase, Func<CancellationToken, Task> phaseAction, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        await phaseAction(cancellationToken);
        pipelineMeter.RecordPhaseDuration(phase, Stopwatch.GetElapsedTime(start));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Source of job {JobId} changed mid-import (ETag/size mismatch); the job is now Failed instead of retrying forever")]
    private partial void LogSourceChanged(Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source of job {JobId} was not found; the job is now Failed instead of retrying forever")]
    private partial void LogSourceNotFound(Guid jobId, Exception exception);

    private async Task PublishCompletedEventAsync(ImportJob job, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await GetStatsWithFallbackAsync(job.Id, cancellationToken);
            var durationMs = job.StartedAtUtc is { } started && job.FinishedAtUtc is { } finished
                ? (finished - started).TotalMilliseconds
                : 0d;
            var completedAt = job.FinishedAtUtc ?? clock.GetUtcNow();

            await eventPublisher.PublishAsync(
                new ImportCompletedEvent(
                    job.Id,
                    job.Source,
                    job.Periodo,
                    job.LinesProcessed,
                    job.LinesRejected,
                    stats.TotalDeudores,
                    stats.TotalEntidades,
                    stats.SumaTotalPrestamos,
                    durationMs,
                    completedAt.UtcDateTime),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEventPublishFailed(job.Id, ex);
        }
    }

    /// <summary>
    /// Reintenta la query de stats (los timeouts transitorios tras una carga pesada son
    /// esperables); si agota los reintentos devuelve stats vacías y lo loguea: el evento
    /// se publica igual porque perder la notificación por un timeout de lectura es peor
    /// que un email con stats degradadas.
    /// </summary>
    private async Task<ImportStats> GetStatsWithFallbackAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await jobStore.GetImportStatsAsync(jobId, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
            {
                LogStatsRetry(jobId, attempt, ex);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStatsUnavailable(jobId, ex);
                return new ImportStats(0, 0, 0);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read import stats for job {JobId} (attempt {Attempt}); retrying before publishing ImportCompletedEvent")]
    private partial void LogStatsRetry(Guid jobId, int attempt, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read import stats for job {JobId} after retries; publishing ImportCompletedEvent with zeroed stats instead of losing the notification")]
    private partial void LogStatsUnavailable(Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to publish ImportCompletedEvent for job {JobId}; the import stays completed")]
    private partial void LogEventPublishFailed(Guid jobId, Exception exception);

    private async Task LoadReferenceTablesAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var ingestion = options.Value;
        var entidades = ResolveReferencePath(job.Source, ingestion.EntidadesReferenceOverride, IngestionOptions.DefaultEntidadesReferenceFile);
        var nombres = ResolveReferencePath(job.Source, ingestion.NombresReferenceOverride, IngestionOptions.DefaultNombresReferenceFile);

        await using var entidadesSource = fileSourceFactory.Create(entidades);
        var entidadesOpened = await entidadesSource.OpenAsync(0, cancellationToken);
        await using var entidadesStream = entidadesOpened.Stream;

        await using var nombresSource = fileSourceFactory.Create(nombres);
        var nombresOpened = await nombresSource.OpenAsync(0, cancellationToken);
        await using var nombresStream = nombresOpened.Stream;

        await referenceStorage.LoadReferenceTablesAsync(
            ReferenceFileParser.ParseEntidadesAsync(entidadesStream, cancellationToken),
            ReferenceFileParser.ParseDeudoresNombresAsync(nombresStream, cancellationToken),
            cancellationToken);
    }

    private static string ResolveReferencePath(string source, string? overridePath, string defaultFileName)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        if (source.Contains("://", StringComparison.Ordinal))
        {
            var separatorIndex = source.LastIndexOf('/');
            return separatorIndex > 0 ? $"{source[..separatorIndex]}/{defaultFileName}" : defaultFileName;
        }

        var directory = Path.GetDirectoryName(source);
        return string.IsNullOrEmpty(directory) ? defaultFileName : Path.Combine(directory, defaultFileName);
    }
}
