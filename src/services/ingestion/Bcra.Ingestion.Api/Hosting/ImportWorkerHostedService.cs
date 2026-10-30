using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Shared.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Api.Hosting;

public sealed partial class ImportWorkerHostedService(
    IServiceProvider serviceProvider,
    IOptions<IngestionOptions> options,
    ILogger<ImportWorkerHostedService> logger) : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    [LoggerMessage(Level = LogLevel.Error, Message = "Import worker iteration failed")]
    private partial void LogIterationFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {JobId} aborted by connection loss; the lease will expire and another worker will reclaim it")]
    private partial void LogAbortedByConnectionLoss(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {JobId} failed: the source changed mid-import (ETag/size mismatch)")]
    private partial void LogFailed(Guid jobId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to refresh jobs-by-status gauge")]
    private partial void LogJobsByStatusRefreshFailed(Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollDelay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var shouldDelay = true;
            try
            {
                ImportProcessingResult result;
                await using (var scope = serviceProvider.CreateAsyncScope())
                {
                    result = await scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                        .TryProcessNextAsync(_workerId, stoppingToken);

                    await RefreshJobsByStatusAsync(scope.ServiceProvider, stoppingToken);
                }

                if (result.Outcome == ImportProcessingOutcome.Completed)
                    shouldDelay = false;
                else if (result.Outcome == ImportProcessingOutcome.AbortedByConnectionLoss && result.JobId is { } jobId)
                    LogAbortedByConnectionLoss(jobId);
                else if (result.Outcome == ImportProcessingOutcome.Failed && result.JobId is { } failedJobId)
                    LogFailed(failedJobId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogIterationFailed(ex);
            }

            if (shouldDelay)
            {
                try
                {
                    await Task.Delay(pollDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Best-effort: refresca el gauge de jobs por estado; si falla, se reintenta en la próxima iteración.</summary>
    private async Task RefreshJobsByStatusAsync(IServiceProvider scopeServices, CancellationToken cancellationToken)
    {
        var meter = scopeServices.GetService<ImportPipelineMeter>();
        if (meter is null)
            return;

        try
        {
            meter.UpdateJobsByStatus(
                await scopeServices.GetRequiredService<IImportJobStore>().CountByStatusAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogJobsByStatusRefreshFailed(ex);
        }
    }
}
