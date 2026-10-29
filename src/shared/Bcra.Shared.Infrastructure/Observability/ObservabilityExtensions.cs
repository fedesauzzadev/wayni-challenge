using Bcra.Shared.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter.Prometheus;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Bcra.Shared.Infrastructure;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Registra OpenTelemetry para un servicio: traces HTTP (ASP.NET Core), métricas runtime,
    /// métricas del pipeline (<see cref="ImportPipelineMeter"/>) y exporter Prometheus en
    /// formato text exposition (scrapeado por Prometheus en <c>/metrics</c>).
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceInstanceId: Environment.MachineName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ImportPipelineMeter.MeterName)
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation());

        return services;
    }

    /// <summary>Expone <c>/metrics</c> en formato Prometheus.</summary>
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
