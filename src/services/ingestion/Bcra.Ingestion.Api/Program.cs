using System.Threading.RateLimiting;
using Bcra.Ingestion.Api.Hosting;
using Bcra.Ingestion.Application;
using Bcra.Ingestion.Application.GetImportStatus;
using Bcra.Ingestion.Application.UploadImport;
using Bcra.Ingestion.Api.UploadImport;
using Bcra.Ingestion.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Logging;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ConfigureDefaults("ingestion-service").ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddSharedInfrastructure()
    .AddIngestionApplication(builder.Configuration)
    .AddIngestionInfrastructure(builder.Configuration);

builder.Services.AddSingleton<StartupState>();
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddHostedService<ImportWorkerHostedService>();

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

app.MapGet("/imports/{id:guid}", async (Guid id, GetImportStatusHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetImportStatusQuery(id), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    })
    .WithName("GetImportStatus")
    .Produces<ImportStatusResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/imports", async (RequestImportRequest request, RequestImportHandler handler, CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new RequestImportCommand(request.Source, request.Etag, request.Size, request.Periodo),
            cancellationToken);

        if (!result.IsSuccess)
            return result.Error.ToProblem();

        var response = result.Value;
        return response.AlreadyExisted
            ? Results.Ok(response)
            : Results.CreatedAtRoute("GetImportStatus", new { id = response.Id }, response);
    })
    .WithName("RequestImport")
    .Produces<ImportRequestResponse>(StatusCodes.Status201Created)
    .Produces<ImportRequestResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.Run();

public sealed partial class Program;
