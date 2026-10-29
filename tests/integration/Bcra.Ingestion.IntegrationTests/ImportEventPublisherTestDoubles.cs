using Bcra.Ingestion.Application.Ports;
using Bcra.Shared.Domain.Contracts;

namespace Bcra.Ingestion.IntegrationTests;

internal sealed class RecordingImportEventPublisher : IImportEventPublisher
{
    public List<ImportCompletedEvent> Events { get; } = [];

    public Task PublishAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken)
    {
        Events.Add(importCompleted);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingImportEventPublisher : IImportEventPublisher
{
    public Task PublishAsync(ImportCompletedEvent importCompleted, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated event-bus outage");
}
