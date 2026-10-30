using System.Threading.RateLimiting;
using Bcra.Query.Application;
using Bcra.Query.Application.GetDeudorByCuit;
using Bcra.Query.Application.GetEntidad;
using Bcra.Query.Application.GetTopDeudores;
using Bcra.Query.Application.GetTotales;
using Bcra.Query.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Logging;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ConfigureDefaults("query-service").ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddSharedInfrastructure()
    .AddObservability("query-service")
    .AddQueryApplication(builder.Configuration)
    .AddQueryInfrastructure(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("default", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60)),
            QueueLimit = 0
        }));
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IExceptionHandler, BadRequestExceptionHandler>();
builder.Services.AddOpenApi(options =>
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

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseRateLimiter();

app.MapObservabilityEndpoints();
app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "BCRA Query API v1"));

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
    .WithName("HealthLive");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJson })
    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
    .WithName("HealthReady");

app.MapGet("/deudores/{cuit}", async (string cuit, GetDeudorByCuitHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetDeudorByCuitQuery(cuit), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetDeudorByCuit")
    .RequireRateLimiting("default")
    .Produces<DeudorResumenResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/deudores", async (int? top, bool? omitirSinNombre, GetTopDeudoresHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTopDeudoresQuery(top, omitirSinNombre ?? false), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetTopDeudores")
    .RequireRateLimiting("default")
    .Produces<IReadOnlyList<DeudorResumenResponse>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/entidades/{codigo:int}", async (int codigo, GetEntidadHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetEntidadQuery(codigo), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetEntidad")
    .RequireRateLimiting("default")
    .Produces<EntidadResumenResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/totales", async (GetTotalesHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTotalesQuery(), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetTotales")
    .RequireRateLimiting("default")
    .Produces<DeudorTotalesResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.Run();

internal sealed class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badHttpRequest)
        {
            return false;
        }

        var statusCode = badHttpRequest.StatusCode is >= 400 and < 500
            ? badHttpRequest.StatusCode
            : StatusCodes.Status400BadRequest;
        await Results.Problem(statusCode: statusCode, title: "Bad Request", detail: badHttpRequest.Message)
            .ExecuteAsync(httpContext);
        return true;
    }
}

public sealed partial class Program;
