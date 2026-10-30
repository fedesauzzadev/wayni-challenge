namespace Bcra.Ingestion.Application.Ports;

/// <summary>Puerto estrecho del ciclo de vida del storage por import (partición, agregación, swap, DROP).</summary>
public interface IImportStorageManager
{
    Task AggregateImportAsync(Guid importId, string situacionExpression, CancellationToken cancellationToken);
    Task CreateImportStorageAsync(Guid importId, CancellationToken cancellationToken);
    Task CreateImportIndexesAsync(Guid importId, CancellationToken cancellationToken);
    Task<Guid?> SwapActiveImportAsync(Guid importId, CancellationToken cancellationToken);
    Task DropImportStorageAsync(Guid importId, CancellationToken cancellationToken);
}
