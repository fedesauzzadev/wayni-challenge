using System.Text.Json;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Shared.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace Bcra.Notifier.Application.Notifications;

public enum MessageProcessingOutcome
{
    Delete,
    Abandon
}

public sealed partial class ImportCompletedMessageHandler(
    IEnumerable<INotifier> notifiers,
    ILogger<ImportCompletedMessageHandler> logger)
{
    public async Task<MessageProcessingOutcome> HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        ImportCompletedEvent? importCompleted;
        try
        {
            importCompleted = JsonSerializer.Deserialize<ImportCompletedEvent>(messageBody, ImportCompletedEventJson.Options);
        }
        catch (JsonException ex)
        {
            LogMalformedMessage(ex.Message);
            return MessageProcessingOutcome.Delete;
        }

        if (importCompleted is null)
        {
            LogMalformedMessage("Deserialized event was null.");
            return MessageProcessingOutcome.Delete;
        }

        foreach (var notifier in notifiers)
        {
            var result = await notifier.NotifyAsync(importCompleted, cancellationToken);
            if (result.IsFailure)
            {
                LogNotificationFailed(importCompleted.JobId, notifier.Strategy, result.Error.ToString());
                return MessageProcessingOutcome.Abandon;
            }

            if (result.Value is { Delivered: false })
            {
                LogNotificationFailed(importCompleted.JobId, notifier.Strategy, result.Value.FailureReason ?? "not delivered");
                return MessageProcessingOutcome.Abandon;
            }
        }

        LogNotified(importCompleted.JobId);
        return MessageProcessingOutcome.Delete;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discarded malformed ImportCompletedEvent message: {Reason}")]
    private partial void LogMalformedMessage(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification via '{Strategy}' failed for job {JobId}: {Reason}")]
    private partial void LogNotificationFailed(Guid jobId, string strategy, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {JobId} notified")]
    private partial void LogNotified(Guid jobId);
}
