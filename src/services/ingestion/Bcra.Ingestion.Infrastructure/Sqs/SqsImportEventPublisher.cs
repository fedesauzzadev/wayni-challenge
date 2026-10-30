using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Bcra.Ingestion.Application.Ports;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Infrastructure.Sqs;

public sealed partial class SqsImportEventPublisher(
    IAmazonSQS sqs,
    IOptions<SqsOptions> options,
    ILogger<SqsImportEventPublisher> logger) : IImportEventPublisher
{
    public async Task PublishAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        var queueUrl = options.Value.ImportCompletedQueueUrl;
        if (string.IsNullOrWhiteSpace(queueUrl))
        {
            LogPublishSkipped();
            return;
        }

        await sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = JsonSerializer.Serialize(importCompleted, ImportCompletedEventJson.Options)
            },
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "ImportCompletedEvent publish skipped: no SQS queue URL configured")]
    private partial void LogPublishSkipped();
}
