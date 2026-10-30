using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Domain.ImportJobs;
using FluentAssertions;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class ImportCompletedEventTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CompletedImport_PublishesImportCompletedEvent_WithJobData()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-events", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 500, rejectedLines: 2);

        var options = new IngestionOptions
        {
            BatchSize = 100,
            AllowedSourceDirs = [directory],
            SituacionStrategy = "max"
        };
        var repository = fixture.CreateRepository();

        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);

        var publisher = new RecordingImportEventPublisher();
        var processor = ImportTestHarness.CreateProcessor(fixture, options, eventPublisher: publisher);

        var result = await processor.TryProcessNextAsync("worker-events", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Completed);
        publisher.Events.Should().ContainSingle();
        var importCompleted = publisher.Events[0];
        importCompleted.JobId.Should().Be(job.Id);
        importCompleted.Source.Should().Be(sample.DeudoresPath);
        importCompleted.Periodo.Should().Be(202311);
        importCompleted.LinesProcessed.Should().Be(sample.ValidLines);
        importCompleted.LinesRejected.Should().Be(sample.RejectedLines);
        importCompleted.LinesSkippedControl.Should().Be(sample.SkippedControlLines);
        importCompleted.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        importCompleted.CompletedAtUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task PublisherFailure_IsBestEffort_AndDoesNotFailTheImport()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-events", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 100);

        var options = new IngestionOptions
        {
            BatchSize = 100,
            AllowedSourceDirs = [directory],
            SituacionStrategy = "max"
        };
        var repository = fixture.CreateRepository();

        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);

        var processor = ImportTestHarness.CreateProcessor(fixture, options, eventPublisher: new ThrowingImportEventPublisher());

        var result = await processor.TryProcessNextAsync("worker-events-throwing", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Completed, "el publish es best-effort y no falla el import");
        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Completed);
    }
}
