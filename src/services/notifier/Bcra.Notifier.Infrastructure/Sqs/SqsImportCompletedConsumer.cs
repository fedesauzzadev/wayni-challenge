using Amazon.SQS;
using Amazon.SQS.Model;
using Bcra.Notifier.Application.Notifications;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure.Sqs;

public sealed partial class SqsImportCompletedConsumer(
    IAmazonSQS sqs,
    IOptions<SqsOptions> options,
    ImportCompletedMessageHandler messageHandler,
    ILogger<SqsImportCompletedConsumer> logger) : BackgroundService
{
    /// <summary>Máximo de segundos que SQS acepta en WaitTimeSeconds (long polling).</summary>
    private const int MaxSqsWaitTimeSeconds = 20;
    /// <summary>Máximo de entradas que SQS acepta por DeleteMessageBatch.</summary>
    private const int MaxSqsBatchDeleteEntries = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = options.Value.ImportCompletedQueueUrl;
        if (string.IsNullOrWhiteSpace(queueUrl))
        {
            LogQueueNotConfigured();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(
                    new ReceiveMessageRequest
                    {
                        QueueUrl = queueUrl,
                        MaxNumberOfMessages = Math.Max(1, options.Value.MaxMessages),
                        WaitTimeSeconds = Math.Clamp(options.Value.WaitTimeSeconds, 0, MaxSqsWaitTimeSeconds)
                    },
                    stoppingToken);

                var messages = response.Messages ?? [];
                if (messages.Count == 0)
                    continue;

                var processed = await Task.WhenAll(messages.Select(message => ProcessMessageAsync(message, stoppingToken)));
                var messagesToDelete = messages
                    .Zip(processed, (message, delete) => (message, delete))
                    .Where(pair => pair.delete)
                    .Select(pair => pair.message)
                    .ToArray();

                if (messagesToDelete.Length > 0)
                    await DeleteMessagesAsync(queueUrl, messagesToDelete, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPollFailed(ex);
                await DelayQuietlyAsync(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollErrorDelaySeconds)), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        MessageProcessingOutcome outcome;
        try
        {
            outcome = await messageHandler.HandleAsync(message.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMessageFailedUnexpectedly(message.MessageId, ex);
            return false;
        }

        if (outcome == MessageProcessingOutcome.Abandon)
        {
            LogMessageAbandoned(message.MessageId);
            return false;
        }

        return true;
    }

    private async Task DeleteMessagesAsync(string queueUrl, IReadOnlyCollection<Message> messages, CancellationToken cancellationToken)
    {
        foreach (var chunk in messages.Chunk(MaxSqsBatchDeleteEntries))
        {
            try
            {
                var response = await sqs.DeleteMessageBatchAsync(
                    new DeleteMessageBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = chunk
                            .Select(message => new DeleteMessageBatchRequestEntry { Id = message.MessageId, ReceiptHandle = message.ReceiptHandle })
                            .ToList()
                    },
                    cancellationToken);

                foreach (var failure in response.Failed)
                    LogDeleteFailedEntry(failure.Id, failure.Code, failure.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var message in chunk)
                    LogDeleteFailed(message.MessageId, ex);
            }
        }
    }

    private static async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SQS import-completed queue URL is not configured; the consumer is idle")]
    private partial void LogQueueNotConfigured();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to receive messages from SQS")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} was not deleted and will return via visibility timeout")]
    private partial void LogMessageAbandoned(string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error processing message {MessageId}; the rest of the batch continues and it will return via visibility timeout")]
    private partial void LogMessageFailedUnexpectedly(string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete message {MessageId} from SQS; it may be notified again")]
    private partial void LogDeleteFailed(string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete message {MessageId} from SQS (code {Code}: {Reason}); it may be notified again")]
    private partial void LogDeleteFailedEntry(string messageId, string? code, string? reason);
}
