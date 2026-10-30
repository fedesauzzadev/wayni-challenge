using Bcra.Query.Domain.ReadModels;

namespace Bcra.Query.Domain.Ports;

public interface IEntidadReadRepository
{
    Task<EntidadResumen?> GetEntidadAsync(Guid importId, int codigo, CancellationToken cancellationToken);
}
