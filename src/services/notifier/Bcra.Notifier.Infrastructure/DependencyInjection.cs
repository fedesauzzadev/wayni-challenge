using Amazon.SQS;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Notifier.Infrastructure.Sqs;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Aws;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifierInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<AwsOptions>(configuration, AwsOptions.SectionName);
        services.AddValidatedOptions<SqsOptions>(configuration, SqsOptions.SectionName);
        services.AddValidatedOptions<NotificationsOptions>(configuration, NotificationsOptions.SectionName);

        services.AddSingleton<IAmazonSQS>(sp =>
            AwsClientFactory.CreateSqsClient(sp.GetRequiredService<IOptions<AwsOptions>>().Value));

        services.AddSingleton<INotifier, StructuredLogNotifier>();

        var webhookUrl = configuration[$"{NotificationsOptions.SectionName}:{nameof(NotificationsOptions.WebhookUrl)}"];
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            services.AddHttpClient(WebhookNotifier.HttpClientName, (sp, client) =>
                client.Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<NotificationsOptions>>().Value.TimeoutSeconds));
            services.AddSingleton<INotifier, WebhookNotifier>();
        }

        services.AddHealthChecks().AddCheck<SqsHealthCheck>("sqs");
        services.AddHostedService<SqsImportCompletedConsumer>();

        return services;
    }
}
