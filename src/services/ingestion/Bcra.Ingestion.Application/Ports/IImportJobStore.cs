using Bcra.Ingestion.Domain.ImportJobs;

namespace Bcra.Ingestion.Application.Ports;

/// <summary>Puerto estrecho de persistencia del agregado <see cref="ImportJob"/>.</summary>
public interface IImportJobStore
{
    Task<(ImportJob Job, bool AlreadyExisted)> AddOrGetExistingAsync(ImportJob job, CancellationToken cancellationToken);
    Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ImportJob?> GetLatestAsync(CancellationToken cancellationToken);
    Task SaveAsync(ImportJob job, CancellationToken cancellationToken);
    Task<ImportJob?> ClaimNextAsync(string leaseOwner, DateTimeOffset utcNow, DateTimeOffset heartbeatCutoff, CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(Guid jobId, string leaseOwner, DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task<ImportStats> GetImportStatsAsync(Guid importId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken);
}
