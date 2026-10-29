using Amazon.SQS;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Infrastructure.Aws;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Notifier.Infrastructure.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AwsOptions>(configuration.GetSection(AwsOptions.SectionName));
        services.Configure<SqsOptions>(configuration.GetSection(SqsOptions.SectionName));
        services.Configure<NotificationsOptions>(configuration.GetSection(NotificationsOptions.SectionName));

        services.AddSingleton<IAmazonSQS>(sp =>
            AwsClientFactory.CreateSqsClient(sp.GetRequiredService<IOptions<AwsOptions>>().Value));

        services.AddSingleton<INotifier, StructuredLogNotifier>();

        var webhookUrl = configuration[$"{NotificationsOptions.SectionName}:{nameof(NotificationsOptions.WebhookUrl)}"];
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            var timeoutSeconds = configuration.GetValue(
                $"{NotificationsOptions.SectionName}:{nameof(NotificationsOptions.TimeoutSeconds)}",
                10);
            services.AddHttpClient(WebhookNotifier.HttpClientName, client =>
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds));
            services.AddSingleton<INotifier, WebhookNotifier>();
        }

        services.AddHealthChecks().AddCheck<SqsHealthCheck>("sqs");

        return services;
    }
}
