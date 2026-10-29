using Amazon.S3;
using Amazon.SQS;
using Bcra.Ingestion.Application.GetImportStatus;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Situations;
using Bcra.Ingestion.Infrastructure.Aws;
using Bcra.Ingestion.Infrastructure.HealthChecks;
using Bcra.Ingestion.Infrastructure.Options;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Persistence.Migrations;
using Bcra.Ingestion.Infrastructure.FileSources;
using Bcra.Ingestion.Infrastructure.Situations;
using Bcra.Ingestion.Infrastructure.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<AwsOptions>(configuration.GetSection(AwsOptions.SectionName));
        services.Configure<SqsOptions>(configuration.GetSection(SqsOptions.SectionName));

        services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new NpgsqlConnectionFactory(options.ConnectionString);
        });

        services.AddSingleton<IAmazonS3>(sp => AwsClientFactory.CreateS3Client(sp.GetRequiredService<IOptions<AwsOptions>>().Value));
        services.AddSingleton<IAmazonSQS>(sp => AwsClientFactory.CreateSqsClient(sp.GetRequiredService<IOptions<AwsOptions>>().Value));

        services.AddSingleton<DbUpMigrator>();
        services.AddScoped<IImportJobRepository, DapperImportJobRepository>();
        services.AddScoped<Bcra.Ingestion.Application.ImportExecution.IStagingBatchCommitter>(sp =>
            (Bcra.Ingestion.Application.ImportExecution.IStagingBatchCommitter)sp.GetRequiredService<IImportJobRepository>());
        services.AddSingleton<LocalFileSourceFactory>();
        services.AddSingleton<Bcra.Ingestion.Application.Ports.IFileSourceFactory, RoutingFileSourceFactory>();
        services.AddScoped<Bcra.Ingestion.Application.Ports.IImportEventPublisher, SqsImportEventPublisher>();

        services.AddSingleton<ISituacionAggregationStrategy>(sp =>
        {
            var ingestion = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Bcra.Ingestion.Application.Options.IngestionOptions>>().Value;
            return ingestion.SituacionStrategy switch
            {
                MaxLiteralSituacionStrategy.StrategyName => new MaxLiteralSituacionStrategy(),
                WorstSeveritySituacionStrategy.StrategyName => new WorstSeveritySituacionStrategy(),
                _ => throw new InvalidOperationException(
                    $"Unknown situacion strategy '{ingestion.SituacionStrategy}'. Valid values: 'max', 'peor_situacion'.")
            };
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        services.AddScoped<GetImportStatusHandler>();

        return services;
    }
}
