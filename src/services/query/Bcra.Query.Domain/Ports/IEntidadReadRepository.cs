using Bcra.Query.Domain.ReadModels;

namespace Bcra.Query.Domain.Ports;

public interface IEntidadReadRepository
{
    Task<EntidadResumen?> GetEntidadAsync(Guid importId, int codigo, CancellationToken cancellationToken);

    Task<EntidadesPage> GetEntidadesPageAsync(Guid importId, string? codigoFilter, CancellationToken cancellationToken);
}
