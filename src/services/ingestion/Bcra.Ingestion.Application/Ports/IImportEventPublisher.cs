using Bcra.Shared.Domain.Contracts;

namespace Bcra.Ingestion.Application.Ports;

public interface IImportEventPublisher
{
    Task PublishAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken);
}
