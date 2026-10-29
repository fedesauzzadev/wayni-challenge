using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace Bcra.Shared.Infrastructure.Logging;

public static class LoggingExtensions
{
    public const string CorrelationIdItemName = "Bcra.CorrelationId";
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    public static LoggerConfiguration ConfigureDefaults(this LoggerConfiguration config, string applicationName)
    {
        config
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.Extensions.Diagnostics", LogEventLevel.Warning)
            .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());

        return config;
    }
}
