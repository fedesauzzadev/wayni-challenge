using Bcra.Ingestion.Application.GetImportStatus;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Situations;
using Bcra.Ingestion.Infrastructure.HealthChecks;
using Bcra.Ingestion.Infrastructure.Options;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Persistence.Migrations;
using Bcra.Ingestion.Infrastructure.FileSources;
using Bcra.Ingestion.Infrastructure.Situations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Ingestion.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
            return new NpgsqlConnectionFactory(options.ConnectionString);
        });

        services.AddSingleton<DbUpMigrator>();
        services.AddScoped<IImportJobRepository, DapperImportJobRepository>();
        services.AddSingleton<Bcra.Ingestion.Application.Ports.IFileSourceFactory, LocalFileSourceFactory>();
        services.AddScoped<Bcra.Ingestion.Application.ImportExecution.IStagingWriter, NpgsqlStagingWriter>();

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
