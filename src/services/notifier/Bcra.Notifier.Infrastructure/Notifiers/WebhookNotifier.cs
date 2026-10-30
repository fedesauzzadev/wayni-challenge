using System.Net.Http.Json;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Domain.Notifications;
using Bcra.Shared.Domain.Contracts;
using Bcra.Shared.Domain.Errors;
using Bcra.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure.Notifiers;

public sealed class WebhookNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationsOptions> options) : INotifier
{
    public const string HttpClientName = "notifier-webhook";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public string Strategy => "webhook";

    public async Task<Result<NotificationReceipt>> NotifyAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        var webhookUrl = options.Value.WebhookUrl;

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
            {
                Content = JsonContent.Create(importCompleted, options: ImportCompletedEventJson.Options)
            };
            request.Headers.Add(IdempotencyKeyHeader, importCompleted.JobId.ToString());

            using var response = await client.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? Result.Ok(new NotificationReceipt(Strategy, true))
                : Result.Fail<NotificationReceipt>(Error.Unavailable(
                    "Webhook.NotDelivered",
                    $"Webhook responded with status code {(int)response.StatusCode}."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<NotificationReceipt>(Error.Unavailable("Webhook.Failed", $"Webhook notification failed: {ex.Message}"));
        }
    }
}
