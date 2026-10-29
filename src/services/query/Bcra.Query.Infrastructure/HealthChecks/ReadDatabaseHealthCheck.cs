using Bcra.Query.Domain.Ports;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bcra.Query.Infrastructure.HealthChecks;

public sealed class ReadDatabaseHealthCheck(IActiveImportResolver activeImportResolver) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var importId = await activeImportResolver.ResolveActiveImportIdAsync(cancellationToken);
            return importId is null
                ? HealthCheckResult.Degraded("No active import yet; queries are unavailable until the first import completes.")
                : HealthCheckResult.Healthy($"Active import: {importId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Read database is not reachable.", ex);
        }
    }
}
