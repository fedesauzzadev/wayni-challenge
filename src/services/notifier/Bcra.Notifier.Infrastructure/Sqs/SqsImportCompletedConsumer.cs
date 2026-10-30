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

                foreach (var message in response.Messages ?? [])
                    await ProcessMessageAsync(queueUrl, message, stoppingToken);
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

    private async Task ProcessMessageAsync(string queueUrl, Message message, CancellationToken cancellationToken)
    {
        var outcome = await messageHandler.HandleAsync(message.Body, cancellationToken);
        if (outcome == MessageProcessingOutcome.Abandon)
        {
            LogMessageAbandoned(message.MessageId);
            return;
        }

        try
        {
            await sqs.DeleteMessageAsync(
                new DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = message.ReceiptHandle },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeleteFailed(message.MessageId, ex);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete message {MessageId} from SQS; it may be notified again")]
    private partial void LogDeleteFailed(string messageId, Exception exception);
}
