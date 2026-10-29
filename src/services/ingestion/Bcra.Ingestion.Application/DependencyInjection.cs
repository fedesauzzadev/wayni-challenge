using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.UploadImport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Ingestion.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIngestionApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RequestImportHandler>();
        services.AddScoped<ImportJobProcessor>();
        services.AddScoped<StagingCopyLoader>();
        return services;
    }
}
