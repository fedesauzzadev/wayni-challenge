using Amazon.SQS;
using Bcra.Notifier.Application.Notifiers;
using Bcra.Notifier.Application.Ports;
using Bcra.Notifier.Infrastructure.Notifiers;
using Bcra.Notifier.Infrastructure.Persistence;
using Bcra.Notifier.Infrastructure.Sqs;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Aws;
using Bcra.Shared.Infrastructure.Options;
using Bcra.Shared.Infrastructure.Persistence;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Notifier.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// <paramref name="isDocumentGeneration"/> replica el patrón del ingestion-api: durante la
    /// generación del documento OpenAPI no hay entorno (BD/SQS), así que se saltean la validación
    /// al arranque de <see cref="DatabaseOptions"/> y el arranque del consumidor SQS (que resuelve
    /// la cadena INotifier → store de destinatarios → conexión a BD).
    /// </summary>
    public static IServiceCollection AddNotifierInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDocumentGeneration = false)
    {
        DapperDefaults.Configure();

        services.AddValidatedOptions<AwsOptions>(configuration, AwsOptions.SectionName);
        services.AddValidatedOptions<SqsOptions>(configuration, SqsOptions.SectionName);
        services.AddValidatedOptions<NotificationsOptions>(configuration, NotificationsOptions.SectionName);
        services.AddValidatedOptions<EmailOptions>(configuration, EmailOptions.SectionName);
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName, validateOnStart: !isDocumentGeneration);

        services.AddSingleton<IAmazonSQS>(sp =>
            AwsClientFactory.CreateSqsClient(sp.GetRequiredService<IOptions<AwsOptions>>().Value));

        // Conexión readonly para leer los destinatarios (tabla creada por las migraciones de ingestion).
        services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new NpgsqlConnectionFactory(options.ConnectionString);
        });
        services.AddSingleton<INotificationRecipientStore, DapperNotificationRecipientStore>();

        services.AddSingleton<INotifier, StructuredLogNotifier>();

        var webhookUrl = configuration[$"{NotificationsOptions.SectionName}:{nameof(NotificationsOptions.WebhookUrl)}"];
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            services.AddHttpClient(WebhookNotifier.HttpClientName, (sp, client) =>
                client.Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<NotificationsOptions>>().Value.TimeoutSeconds));
            services.AddSingleton<INotifier, WebhookNotifier>();
        }

        services.AddSingleton<ISmtpTransport, MailKitSmtpTransport>();
        services.AddSingleton<INotifier, EmailNotifier>();

        services.AddHealthChecks().AddCheck<SqsHealthCheck>("sqs");

        if (!isDocumentGeneration)
        {
            services.AddHostedService<SqsImportCompletedConsumer>();
        }

        return services;
    }
}
