using Bcra.Query.Application;
using Bcra.Query.Application.GetDeudorByCuit;
using Bcra.Query.Application.GetEntidad;
using Bcra.Query.Application.GetTopDeudores;
using Bcra.Query.Application.GetTotales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bcra.Query.Application;

public sealed class QueryOptions
{
    public const string SectionName = "Query";

    public int TopMax { get; set; } = 100;
}

public static class DependencyInjection
{
    public static IServiceCollection AddQueryApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueryOptions>(configuration.GetSection(QueryOptions.SectionName));

        services.AddScoped<GetDeudorByCuitHandler>();
        services.AddScoped<GetTopDeudoresHandler>();
        services.AddScoped<GetEntidadHandler>();
        services.AddScoped<GetTotalesHandler>();

        return services;
    }
}
