using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed partial class StructuredLogNotifier(ILogger<StructuredLogNotifier> logger) : INotifier
{
    public string Strategy => "structured-log";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Import completed. JobId={JobId} Periodo={Periodo} Source={Source} LinesProcessed={LinesProcessed} " +
                  "LinesRejected={LinesRejected} TotalDeudores={TotalDeudores} TotalEntidades={TotalEntidades} " +
                  "SumaTotalPrestamos={SumaTotalPrestamos} DurationMs={DurationMs} CompletedAtUtc={CompletedAtUtc}")]
    private partial void LogImportCompleted(
        Guid jobId,
        int? periodo,
        string source,
        long linesProcessed,
        long linesRejected,
        long totalDeudores,
        long totalEntidades,
        decimal sumaTotalPrestamos,
        double durationMs,
        DateTime completedAtUtc);

    public Task<Result<NotificationReceipt>> NotifyAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        LogImportCompleted(
            importCompleted.JobId,
            importCompleted.Periodo,
            importCompleted.Source,
            importCompleted.LinesProcessed,
            importCompleted.LinesRejected,
            importCompleted.TotalDeudores,
            importCompleted.TotalEntidades,
            importCompleted.SumaTotalPrestamos,
            importCompleted.DurationMs,
            importCompleted.CompletedAtUtc);

        return Task.FromResult(Result.Ok(new NotificationReceipt(Strategy, true)));
    }
}
