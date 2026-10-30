using Bcra.Ingestion.Api.Hosting;
using Bcra.Ingestion.Api.UploadImport;
using Bcra.Ingestion.Application;
using Bcra.Ingestion.Application.GetImportStatus;
using Bcra.Ingestion.Application.UploadImport;
using Bcra.Ingestion.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddDefaultApi("ingestion-service");

builder.Services
    .AddIngestionApplication(builder.Configuration)
    .AddIngestionInfrastructure(builder.Configuration);

var isDocumentGeneration = Environment.CommandLine.Contains("getdocument", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton<StartupState>();

if (!isDocumentGeneration)
{
    builder.Services.AddHostedService<MigrationHostedService>();
    builder.Services.AddHostedService<ImportWorkerHostedService>();
}

var app = builder.Build();

app.MapDefaultServiceEndpoints("BCRA Ingestion API v1");

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
