namespace Bcra.Query.Domain.Ports;

public interface IActiveImportResolver
{
    Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken);
}
