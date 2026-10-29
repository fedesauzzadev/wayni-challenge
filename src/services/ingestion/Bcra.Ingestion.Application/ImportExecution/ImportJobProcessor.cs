using System.Data.Common;
using Bcra.Ingestion.Domain.ImportJobs;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Application.ImportExecution;

public enum ImportProcessingOutcome
{
    NothingToClaim,
    Completed,
    AbortedByConnectionLoss
}

public sealed record ImportProcessingResult(ImportProcessingOutcome Outcome, Guid? JobId)
{
    public static ImportProcessingResult Nothing { get; } = new(ImportProcessingOutcome.NothingToClaim, null);
}

/// <summary>
/// Orquesta la ejecución de un import bajo lease: reclamo SKIP LOCKED, almacenamiento por import,
/// índices pre-swap, swap atómico de <c>active_import</c> y limpieza por DROP del import anterior.
/// Si se pierde la conexión que sostiene el trabajo, aborta sin confirmar nada más: el lease
/// queda sin heartbeat y otro worker puede reclamar el job al vencer (ADR-005).
/// </summary>
public sealed class ImportJobProcessor(
    IImportJobRepository repository,
    TimeProvider clock,
    IOptions<Options.IngestionOptions> options)
{
    public async Task<ImportProcessingResult> TryProcessNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leaseTimeout = TimeSpan.FromSeconds(options.Value.LeaseTimeoutSeconds);

        var job = await repository.ClaimNextAsync(workerId, now, now - leaseTimeout, cancellationToken);
        if (job is null)
            return ImportProcessingResult.Nothing;

        try
        {
            await repository.CreateImportStorageAsync(job.Id, cancellationToken);

            job.MarkAggregating();
            await repository.SaveAsync(job, cancellationToken);
            await repository.RenewLeaseAsync(job.Id, workerId, clock.GetUtcNow(), cancellationToken);

            await repository.CreateImportIndexesAsync(job.Id, cancellationToken);

            var previousImportId = await repository.SwapActiveImportAsync(job.Id, cancellationToken);

            job.MarkCompleted(clock.GetUtcNow());
            await repository.SaveAsync(job, cancellationToken);

            if (previousImportId is { } previous && previous != job.Id)
                await repository.DropImportStorageAsync(previous, cancellationToken);

            return new ImportProcessingResult(ImportProcessingOutcome.Completed, job.Id);
        }
        catch (DbException)
        {
            return new ImportProcessingResult(ImportProcessingOutcome.AbortedByConnectionLoss, job.Id);
        }
    }
}
