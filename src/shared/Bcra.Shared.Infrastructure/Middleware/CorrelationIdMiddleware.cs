using Bcra.Shared.Infrastructure.Logging;
using Serilog.Context;

namespace Bcra.Shared.Infrastructure.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(LoggingExtensions.CorrelationIdHeaderName, out var received)
            ? received.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[LoggingExtensions.CorrelationIdItemName] = correlationId;
        context.Response.Headers[LoggingExtensions.CorrelationIdHeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
