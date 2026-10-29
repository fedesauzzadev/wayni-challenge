using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.ImportExecution;
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
                }

                if (result.Outcome == ImportProcessingOutcome.Completed)
                    shouldDelay = false;
                else if (result.Outcome == ImportProcessingOutcome.AbortedByConnectionLoss && result.JobId is { } jobId)
                    LogAbortedByConnectionLoss(jobId);
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
}
