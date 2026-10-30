using Bcra.Ingestion.Api.Hosting;
using Bcra.Ingestion.Api.UploadImport;
using Bcra.Ingestion.Application;
using Bcra.Ingestion.Application.GetImportStatus;
using Bcra.Ingestion.Application.UploadImport;
using Bcra.Ingestion.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddDefaultApi("ingestion-service");

builder.Services
    .AddIngestionApplication(builder.Configuration)
    .AddIngestionInfrastructure(builder.Configuration);

var uploadOptions = builder.Services.AddOptions<UploadOptions>()
    .Bind(builder.Configuration.GetSection(UploadOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>();
builder.Services.AddScoped<UploadApiKeyEndpointFilter>();

const string DocumentGenerationArgument = "getdocument";

var isDocumentGeneration = Environment.CommandLine.Contains(DocumentGenerationArgument, StringComparison.OrdinalIgnoreCase);

if (!isDocumentGeneration)
{
    uploadOptions.ValidateOnStart();
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
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
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
    .RequireRateLimiting(DefaultApiExtensions.DefaultRateLimitPolicy)
    .AddEndpointFilter<UploadApiKeyEndpointFilter>()
    .Produces<ImportRequestResponse>(StatusCodes.Status201Created)
    .Produces<ImportRequestResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized);

app.Run();

public sealed partial class Program;
