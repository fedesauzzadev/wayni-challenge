using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class ImportJobProcessorLoggingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DbException_IsLoggedAsWarning_BeforeReturningAbortedByConnectionLoss()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 300);

        var options = new IngestionOptions
        {
            BatchSize = 50,
            AllowedSourceDirs = [directory],
            LeaseTimeoutSeconds = 60,
            SituacionStrategy = "max"
        };
        var repository = fixture.CreateRepository();

        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);

        var logger = new SpyLogger<ImportJobProcessor>();
        var processor = ImportTestHarness.CreateProcessor(
            fixture,
            options,
            batchCommitter: new FaultingStagingBatchCommitter(repository, failOnCommitCall: 3),
            logger: logger);

        var result = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.AbortedByConnectionLoss);
        result.JobId.Should().Be(candidate.Id);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Exception is NpgsqlException,
            "el retry-loop silencioso ahora loguea la DbException antes de abortar");
    }
}

internal sealed class SpyLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}
