using Bcra.Query.Domain.Ports;
using Bcra.Query.Infrastructure.HealthChecks;
using Bcra.Query.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Query.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQueryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddSingleton<IReadOnlyDbConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
            return new ReadonlyNpgsqlConnectionFactory(options.ConnectionString);
        });

        services.AddScoped<IActiveImportResolver, ActiveImportResolver>();
        services.AddScoped<IDeudorReadRepository, DapperDeudorReadRepository>();
        services.AddScoped<IEntidadReadRepository, DapperEntidadReadRepository>();
        services.AddHealthChecks().AddCheck<ReadDatabaseHealthCheck>("read-database");

        return services;
    }
}
