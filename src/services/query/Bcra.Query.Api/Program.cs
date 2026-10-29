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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ConfigureDefaults("query-service").ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddSharedInfrastructure()
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
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCorrelationId();
app.UseRateLimiter();

app.MapOpenApi();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJson });

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

app.MapGet("/deudores", async (int? top, GetTopDeudoresHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTopDeudoresQuery(top), cancellationToken);
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

public sealed partial class Program;
