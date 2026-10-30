using Bcra.Query.Domain.Ports;
using Bcra.Query.Infrastructure.HealthChecks;
using Bcra.Query.Infrastructure.Persistence;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Options;
using Bcra.Shared.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bcra.Query.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQueryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperDefaults.Configure();

        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName);
        services.AddValidatedOptions<ActiveImportCacheOptions>(configuration, ActiveImportCacheOptions.SectionName);

        services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return new NpgsqlConnectionFactory(options.ConnectionString);
        });

        services.AddMemoryCache();
        services.AddScoped<ActiveImportResolver>();
        services.AddScoped<IActiveImportResolver>(sp =>
        {
            var ttlSeconds = sp.GetRequiredService<IOptions<ActiveImportCacheOptions>>().Value.ActiveImportCacheTtlSeconds;
            return ttlSeconds > 0
                ? new CachedActiveImportResolver(
                    sp.GetRequiredService<ActiveImportResolver>(),
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<IOptions<ActiveImportCacheOptions>>())
                : sp.GetRequiredService<ActiveImportResolver>();
        });
        services.AddScoped<IDeudorReadRepository, DapperDeudorReadRepository>();
        services.AddScoped<IEntidadReadRepository, DapperEntidadReadRepository>();
        services.AddHealthChecks().AddCheck<ReadDatabaseHealthCheck>("read-database");

        return services;
    }
}
