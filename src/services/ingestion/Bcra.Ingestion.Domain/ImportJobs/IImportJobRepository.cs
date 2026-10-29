namespace Bcra.Ingestion.Domain.ImportJobs;

public interface IImportJobRepository
{
    Task<(ImportJob Job, bool AlreadyExisted)> AddOrGetExistingAsync(ImportJob job, CancellationToken cancellationToken);
    Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(ImportJob job, CancellationToken cancellationToken);
    Task<ImportJob?> ClaimNextAsync(string leaseOwner, DateTimeOffset utcNow, DateTimeOffset heartbeatCutoff, CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(Guid jobId, string leaseOwner, DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task LoadReferenceTablesAsync(Stream entidades, Stream nombres, CancellationToken cancellationToken);
    Task AggregateImportAsync(Guid importId, string situacionExpression, CancellationToken cancellationToken);
    Task CreateImportStorageAsync(Guid importId, CancellationToken cancellationToken);
    Task CreateImportIndexesAsync(Guid importId, CancellationToken cancellationToken);
    Task<Guid?> SwapActiveImportAsync(Guid importId, CancellationToken cancellationToken);
    Task DropImportStorageAsync(Guid importId, CancellationToken cancellationToken);
    Task<ImportStats> GetImportStatsAsync(Guid importId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken);
}
