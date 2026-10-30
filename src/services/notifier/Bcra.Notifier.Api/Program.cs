using Bcra.Notifier.Application;
using Bcra.Notifier.Infrastructure;
using Bcra.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddDefaultApi("notifier-service", useRateLimiter: false);

const string DocumentGenerationArgument = "getdocument";

var isDocumentGeneration = Environment.CommandLine.Contains(DocumentGenerationArgument, StringComparison.OrdinalIgnoreCase);

builder.Services
    .AddNotifierApplication()
    .AddNotifierInfrastructure(builder.Configuration, isDocumentGeneration);

var app = builder.Build();

app.MapDefaultServiceEndpoints("BCRA Notifier API v1", useRateLimiter: false);

app.Run();

public sealed partial class Program;
