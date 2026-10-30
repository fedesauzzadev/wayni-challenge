using System.Threading.RateLimiting;
using Bcra.Shared.Infrastructure.Logging;
using Bcra.Shared.Infrastructure.Observability;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Serilog;

namespace Bcra.Shared.Infrastructure;

public static class DefaultApiExtensions
{
    /// <summary>Nombre de la política fixed-window por IP aplicada por defecto.</summary>
    public const string DefaultRateLimitPolicy = "default";

    private const string UnknownClientKey = "unknown";
    private const int DefaultPermitLimit = 100;
    private const int DefaultWindowSeconds = 60;

    /// <summary>
    /// Bootstrap comun de los servicios: Serilog con <see cref="LoggingExtensions.ConfigureDefaults"/>,
    /// infra compartida, observabilidad, ProblemDetails, OpenAPI 3.1 (con document transformer de
    /// <c>/health/*</c>) y rate limiter fixed-window por IP (opt-out via <paramref name="useRateLimiter"/>).
    /// </summary>
    public static WebApplicationBuilder AddDefaultApi(this WebApplicationBuilder builder, string serviceName, bool useRateLimiter = true)
    {
        builder.Host.UseSerilog((context, configuration) =>
            configuration.ConfigureDefaults(serviceName).ReadFrom.Configuration(context.Configuration));

        builder.Services
            .AddSharedInfrastructure()
            .AddObservability(serviceName)
            .AddProblemDetails()
            .AddDefaultOpenApi();

        if (useRateLimiter)
        {
            builder.Services.AddDefaultRateLimiter(builder.Configuration);
        }

        return builder;
    }

    /// <summary>
    /// Pipeline y endpoints comunes: middleware de excepciones/correlation/request-logging
    /// (y rate limiter si se registro), <c>/metrics</c>, OpenAPI + SwaggerUI y health checks live/ready.
    /// </summary>
    public static WebApplication MapDefaultServiceEndpoints(this WebApplication app, string swaggerTitle, bool useRateLimiter = true)
    {
        app.UseExceptionHandler();
        app.UseCorrelationId();
        app.UseSerilogRequestLogging();

        if (useRateLimiter)
        {
            app.UseRateLimiter();
        }

        app.MapObservabilityEndpoints();
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", swaggerTitle));

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
            .WithName("HealthLive");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJson })
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
            .WithName("HealthReady");

        return app;
    }

    private static void AddDefaultOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Paths ??= new OpenApiPaths();

                foreach (var (path, summary) in new[] { ("/health/live", "Liveness probe"), ("/health/ready", "Readiness probe") })
                {
                    document.Paths.TryAdd(path, new OpenApiPathItem
                    {
                        Operations = new Dictionary<HttpMethod, OpenApiOperation>
                        {
                            [HttpMethod.Get] = new OpenApiOperation
                            {
                                Summary = summary,
                                Responses = new OpenApiResponses
                                {
                                    ["200"] = new OpenApiResponse { Description = "Healthy" }
                                }
                            }
                        }
                    });
                }

                return Task.CompletedTask;
            });
        });
    }

    private static void AddDefaultRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(DefaultRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? UnknownClientKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimiting:PermitLimit", DefaultPermitLimit),
                    Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:WindowSeconds", DefaultWindowSeconds)),
                    QueueLimit = 0
                }));
        });
    }
}
