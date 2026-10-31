using Bcra.Query.Application;
using Bcra.Query.Application.Contracts;
using Bcra.Query.Application.GetDeudorByCuit;
using Bcra.Query.Application.GetEntidad;
using Bcra.Query.Application.GetEntidades;
using Bcra.Query.Application.GetTopDeudores;
using Bcra.Query.Application.GetTotales;
using Bcra.Query.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddDefaultApi("query-service");

builder.Services
    .AddQueryApplication(builder.Configuration)
    .AddQueryInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IExceptionHandler, BadRequestExceptionHandler>();

var app = builder.Build();

app.MapDefaultServiceEndpoints("BCRA Query API v1");

app.MapGet("/deudores/{cuit}", async (string cuit, GetDeudorByCuitHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetDeudorByCuitQuery(cuit), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetDeudorByCuit")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .Produces<DeudorResumenResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/deudores", async (int? offset, int? limit, int? top, bool? omitirSinNombre, int? situacionMaxima, GetTopDeudoresHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTopDeudoresQuery(offset, limit, top, omitirSinNombre ?? false, situacionMaxima), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetTopDeudores")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .Produces<DeudoresPageResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Ruta literal del enunciado (GET /deudores/top/{n}): alias del top por query param; mismo
// envelope paginado (offset 0, limit n acotado por Query:TopMax).
app.MapGet("/deudores/top/{n:int}", async (int n, GetTopDeudoresHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTopDeudoresQuery(Offset: 0, Limit: null, Top: n, OmitirSinNombre: false), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetTopDeudoresTopN")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .Produces<DeudoresPageResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/entidades/{codigo:int}", async (int codigo, GetEntidadHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetEntidadQuery(codigo), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetEntidad")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .Produces<EntidadResumenResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/entidades", async (string? codigo, GetEntidadesHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetEntidadesQuery(codigo), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetEntidades")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .Produces<EntidadesPageResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/totales", async (GetTotalesHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetTotalesQuery(), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetTotales")
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
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
