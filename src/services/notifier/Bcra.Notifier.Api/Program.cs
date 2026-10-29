using Bcra.Notifier.Application;
using Bcra.Notifier.Infrastructure;
using Bcra.Shared.Infrastructure;
using Bcra.Shared.Infrastructure.Logging;
using Bcra.Shared.Infrastructure.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ConfigureDefaults("notifier-service").ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddSharedInfrastructure()
    .AddNotifierApplication(builder.Configuration)
    .AddNotifierInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
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
app.UseSerilogRequestLogging();
app.UseCorrelationId();

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "BCRA Notifier API v1"));

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
    .WithName("HealthLive");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJson })
    .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK))
    .WithName("HealthReady");

app.Run();

public sealed partial class Program;
