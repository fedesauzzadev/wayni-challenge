using Bcra.Notifier.Application;
using Bcra.Notifier.Infrastructure;
using Bcra.Notifier.Infrastructure.Sqs;
using Bcra.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddDefaultApi("notifier-service", useRateLimiter: false);

builder.Services
    .AddNotifierApplication(builder.Configuration)
    .AddNotifierInfrastructure(builder.Configuration);

builder.Services.AddHostedService<SqsImportCompletedConsumer>();

var app = builder.Build();

app.MapDefaultServiceEndpoints("BCRA Notifier API v1", useRateLimiter: false);

app.Run();

public sealed partial class Program;
