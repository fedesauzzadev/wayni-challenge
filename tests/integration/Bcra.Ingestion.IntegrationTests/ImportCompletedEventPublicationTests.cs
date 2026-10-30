using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Infrastructure.Persistence;
using FluentAssertions;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

/// <summary>
/// Regresión del incidente "import completado sin email": un timeout transitorio al leer
/// las stats hacía que el ImportCompletedEvent se perdiera (try/catch best-effort) y la
/// notificación nunca saliera. Ahora se reintenta y, si sigue fallando, se publica el
/// evento con stats en cero en vez de perderlo.
/// </summary>
[Collection("postgres")]
public sealed class ImportCompletedEventPublicationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TransientStatsFailure_AfterRetries_PublishesEventWithRealStats()
    {
        await fixture.ResetAsync();
        var (sample, options) = CreateSample();
        var repository = fixture.CreateRepository();
        var job = await EnqueueJobAsync(repository, sample);

        var publisher = new RecordingImportEventPublisher();
        var processor = ImportTestHarness.CreateProcessor(
            fixture,
            options,
            eventPublisher: publisher,
            jobStore: new FlakyStatsJobStore(repository, failTimes: 2));

        var result = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Completed);
        publisher.Events.Should().HaveCount(1);
        var published = publisher.Events[0];
        published.JobId.Should().Be(job.Id);
        published.LinesProcessed.Should().Be(sample.ValidLines);
        published.TotalDeudores.Should().BeGreaterThan(0, "el tercer intento devuelve las stats reales");
    }

    [Fact]
    public async Task PersistentStatsFailure_PublishesEventWithZeroedStats_InsteadOfLosingIt()
    {
        await fixture.ResetAsync();
        var (sample, options) = CreateSample();
        var repository = fixture.CreateRepository();
        var job = await EnqueueJobAsync(repository, sample);

        var publisher = new RecordingImportEventPublisher();
        var processor = ImportTestHarness.CreateProcessor(
            fixture,
            options,
            eventPublisher: publisher,
            jobStore: new FlakyStatsJobStore(repository, failTimes: int.MaxValue));

        var result = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Completed, "el import no falla por la notificación");
        publisher.Events.Should().HaveCount(1, "el evento se publica igual, con stats degradadas");
        var published = publisher.Events[0];
        published.JobId.Should().Be(job.Id);
        published.TotalDeudores.Should().Be(0);
        published.TotalEntidades.Should().Be(0);
        published.SumaTotalPrestamos.Should().Be(0);

        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Completed);
    }

    private static (SampleFiles.Sample Sample, IngestionOptions Options) CreateSample()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 20);
        var options = new IngestionOptions
        {
            BatchSize = 10,
            AllowedSourceDirs = [directory],
            LeaseTimeoutSeconds = 60,
            SituacionStrategy = "max"
        };
        return (sample, options);
    }

    private static async Task<ImportJob> EnqueueJobAsync(DapperImportJobRepository repository, SampleFiles.Sample sample)
    {
        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);
        return job;
    }
}

/// <summary>Falla GetImportStatsAsync las primeras N veces (timeout transitorio simulado).</summary>
internal sealed class FlakyStatsJobStore(IImportJobStore inner, int failTimes) : IImportJobStore
{
    private int _statsCalls;

    public Task<(ImportJob Job, bool AlreadyExisted)> AddOrGetExistingAsync(ImportJob job, CancellationToken cancellationToken) =>
        inner.AddOrGetExistingAsync(job, cancellationToken);

    public Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<ImportJob?> GetLatestAsync(CancellationToken cancellationToken) =>
        inner.GetLatestAsync(cancellationToken);

    public Task SaveAsync(ImportJob job, CancellationToken cancellationToken) =>
        inner.SaveAsync(job, cancellationToken);

    public Task<ImportJob?> ClaimNextAsync(string leaseOwner, DateTimeOffset utcNow, DateTimeOffset heartbeatCutoff, CancellationToken cancellationToken) =>
        inner.ClaimNextAsync(leaseOwner, utcNow, heartbeatCutoff, cancellationToken);

    public Task<bool> RenewLeaseAsync(Guid jobId, string leaseOwner, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        inner.RenewLeaseAsync(jobId, leaseOwner, utcNow, cancellationToken);

    public async Task<ImportStats> GetImportStatsAsync(Guid importId, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _statsCalls) <= failTimes)
            throw new NpgsqlException("Simulated transient timeout reading stats");
        return await inner.GetImportStatsAsync(importId, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
        inner.CountByStatusAsync(cancellationToken);
}
