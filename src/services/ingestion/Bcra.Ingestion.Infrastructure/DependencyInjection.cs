using Amazon.S3;
using Amazon.SQS;
using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Infrastructure.FileSources;
using Bcra.Ingestion.Infrastructure.HealthChecks;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Persistence.Migrations;
using Bcra.Ingestion.Infrastructure.Situations;
using Bcra.Ingestion.Infrastructure.Sqs;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Aws;
using Bcra.Shared.Infrastructure.Options;
using Bcra.Shared.Infrastructure.Persistence;
using Bcra.Shared.Infrastructure.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperDefaults.Configure();

        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName);
        services.AddValidatedOptions<AwsOptions>(configuration, AwsOptions.SectionName);
        services.AddValidatedOptions<SqsOptions>(configuration, SqsOptions.SectionName);

        services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new NpgsqlConnectionFactory(options.ConnectionString);
        });

        services.AddSingleton<IAmazonS3>(sp => AwsClientFactory.CreateS3Client(sp.GetRequiredService<IOptions<AwsOptions>>().Value));
        services.AddSingleton<IAmazonSQS>(sp => AwsClientFactory.CreateSqsClient(sp.GetRequiredService<IOptions<AwsOptions>>().Value));

        services.AddSingleton<DbUpMigrator>();
        services.AddScoped<DapperImportJobRepository>();
        services.AddScoped<IImportJobStore>(sp => sp.GetRequiredService<DapperImportJobRepository>());
        services.AddScoped<IImportStorageManager>(sp => (IImportStorageManager)sp.GetRequiredService<DapperImportJobRepository>());
        services.AddScoped<IReferenceTableStorage>(sp => (IReferenceTableStorage)sp.GetRequiredService<DapperImportJobRepository>());
        services.AddScoped<IStagingBatchCommitter>(sp => (IStagingBatchCommitter)sp.GetRequiredService<DapperImportJobRepository>());
        services.AddSingleton<LocalFileSourceFactory>();
        services.AddSingleton<IFileSourceFactory, RoutingFileSourceFactory>();
        services.AddScoped<IImportEventPublisher, SqsImportEventPublisher>();

        services.AddSingleton<ISituacionAggregationStrategy>(sp =>
        {
            var ingestion = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            return ingestion.SituacionStrategy switch
            {
                MaxLiteralSituacionStrategy.StrategyName => new MaxLiteralSituacionStrategy(),
                WorstSeveritySituacionStrategy.StrategyName => new WorstSeveritySituacionStrategy(),
                _ => throw new InvalidOperationException(
                    $"Unknown situacion strategy '{ingestion.SituacionStrategy}'. Valid values: '{MaxLiteralSituacionStrategy.StrategyName}', '{WorstSeveritySituacionStrategy.StrategyName}'.")
            };
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
