using Amazon.SQS;
using Amazon.SQS.Model;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure.Sqs;

public sealed class SqsHealthCheck(IAmazonSQS sqs, IOptions<SqsOptions> options) : IHealthCheck
{
    private const string QueueArnAttribute = "QueueArn";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var queueUrl = options.Value.ImportCompletedQueueUrl;
        if (string.IsNullOrWhiteSpace(queueUrl))
            return HealthCheckResult.Degraded("SQS import-completed queue URL is not configured.");

        try
        {
            var response = await sqs.GetQueueAttributesAsync(
                new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = [QueueArnAttribute] },
                cancellationToken);

            return response.HttpStatusCode == System.Net.HttpStatusCode.OK
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"SQS returned status code {(int)response.HttpStatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQS queue is not reachable.", ex);
        }
    }
}
