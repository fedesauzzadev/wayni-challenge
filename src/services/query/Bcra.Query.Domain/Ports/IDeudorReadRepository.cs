using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Domain.ValueObjects;

namespace Bcra.Query.Domain.Ports;

public interface IDeudorReadRepository
{
    Task<DeudorResumen?> GetDeudorAsync(Guid importId, Cuit cuit, CancellationToken cancellationToken);

    Task<DeudoresPage> GetDeudoresPageAsync(Guid importId, int offset, int limit, bool omitirSinNombre, CancellationToken cancellationToken);

    Task<DeudorTotales> GetTotalesAsync(Guid importId, CancellationToken cancellationToken);
}
