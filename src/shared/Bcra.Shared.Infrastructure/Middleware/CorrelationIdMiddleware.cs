using System.Diagnostics;
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

        var activity = Activity.Current;
        if (activity is not null)
            activity.SetTag("correlation_id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TraceId", activity?.TraceId.ToString()))
        {
            await _next(context);
        }
    }
}
