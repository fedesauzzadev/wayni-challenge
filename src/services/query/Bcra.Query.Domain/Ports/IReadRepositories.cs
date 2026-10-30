using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Query.Domain.Ports;

public interface IDeudorReadRepository
{
    Task<DeudorResumen?> GetDeudorAsync(Guid importId, Cuit cuit, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeudorResumen>> GetTopDeudoresAsync(Guid importId, int count, bool omitirSinNombre, CancellationToken cancellationToken);

    Task<DeudorTotales> GetTotalesAsync(Guid importId, CancellationToken cancellationToken);
}

public interface IEntidadReadRepository
{
    Task<EntidadResumen?> GetEntidadAsync(Guid importId, int codigo, CancellationToken cancellationToken);
}

public interface IActiveImportResolver
{
    Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken);
}
