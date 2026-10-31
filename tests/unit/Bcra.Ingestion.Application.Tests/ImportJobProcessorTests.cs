using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Parsing;
using Bcra.Shared.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bcra.Ingestion.Application.Tests;

/// <summary>
/// Regresión del loop infinito: antes de estos catches, una fuente fuera de la allow-list
/// (<see cref="UnauthorizedAccessException"/>) o truncada por debajo del checkpoint
/// (<see cref="ArgumentOutOfRangeException"/>) escapaban al catch genérico del worker, el job
/// quedaba Loading con lease vencido y se re-clamaba para siempre sin pasar a Failed.
/// </summary>
public sealed class ImportJobProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IImportJobStore _jobStore = Substitute.For<IImportJobStore>();
    private readonly IImportStorageManager _storageManager = Substitute.For<IImportStorageManager>();
    private readonly IReferenceTableStorage _referenceStorage = Substitute.For<IReferenceTableStorage>();
    private readonly IFileSourceFactory _fileSourceFactory = Substitute.For<IFileSourceFactory>();
    private readonly ISituacionAggregationStrategy _situacionStrategy = Substitute.For<ISituacionAggregationStrategy>();
    private readonly IImportEventPublisher _eventPublisher = Substitute.For<IImportEventPublisher>();

    private ImportJobProcessor CreateProcessor(int batchSize = 10) =>
        new(
            _jobStore,
            _storageManager,
            _referenceStorage,
            _fileSourceFactory,
            new StagingCopyLoader(new CommitRecorder(), new FixedTimeProvider(Now), Microsoft.Extensions.Options.Options.Create(new IngestionOptions { BatchSize = batchSize }), new ImportPipelineMeter()),
            _situacionStrategy,
            _eventPublisher,
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Options.Options.Create(new IngestionOptions { BatchSize = batchSize, LeaseTimeoutSeconds = 60 }),
            new ImportPipelineMeter(),
            NullLogger<ImportJobProcessor>.Instance);

    private static ImportJob ClaimedLoadingJob(long bytesCommitted = 0, long size = 0) =>
        ImportJob.Rehydrate(new ImportJobSnapshot(
            Guid.NewGuid(),
            "/data/deudores.txt",
            Etag: "etag-cliente",
            Size: size,
            Periodo: 202610,
            ImportJobStatus.Loading,
            BytesCommitted: bytesCommitted,
            LinesProcessed: 0,
            LinesRejected: 0,
            LeaseOwner: "worker-1",
            HeartbeatAt: Now,
            CreatedAtUtc: Now.AddMinutes(-1),
            StartedAtUtc: Now.AddMinutes(-1),
            FinishedAtUtc: null,
            SourceEtag: "source-etag-1"));

    private void SetupClaim(ImportJob job) =>
        _jobStore.ClaimNextAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(job);

    [Fact]
    public async Task TryProcessNextAsync_WhenSourceIsOutsideAllowList_MarksJobFailed_InsteadOfRetryingForever()
    {
        var job = ClaimedLoadingJob();
        SetupClaim(job);
        _fileSourceFactory.Create(Arg.Any<string>())
            .Throws(new UnauthorizedAccessException("Source is outside the allowed source directories."));

        var result = await CreateProcessor().TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Failed);
        result.JobId.Should().Be(job.Id);
        job.Status.Should().Be(ImportJobStatus.Failed, "el job pasa a Failed y deja de ser reclamable");
        await _jobStore.Received(1).SaveAsync(
            Arg.Is<ImportJob>(j => j.Id == job.Id && j.Status == ImportJobStatus.Failed),
            Arg.Any<CancellationToken>());
        await _storageManager.DidNotReceiveWithAnyArgs().AggregateImportAsync(default, default!, default);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task TryProcessNextAsync_WhenSourceShrunkBelowCheckpoint_MarksJobFailed_InsteadOfRetryingForever()
    {
        // Checkpoint confirmado en el byte 5000, pero la fuente fue truncada/reemplazada por una
        // de 100 bytes: el open en el offset del checkpoint lanza antes de que ObserveSource
        // pueda detectar el cambio de tamaño (mismo comportamiento que LocalFileSource/S3FileSource).
        var job = ClaimedLoadingJob(bytesCommitted: 5_000, size: 5_000);
        SetupClaim(job);
        _fileSourceFactory.Create(Arg.Any<string>()).Returns(new ShrunkFileSource(currentSize: 100));

        var result = await CreateProcessor().TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Failed);
        result.JobId.Should().Be(job.Id);
        job.Status.Should().Be(ImportJobStatus.Failed);
        await _jobStore.Received(1).SaveAsync(
            Arg.Is<ImportJob>(j => j.Id == job.Id && j.Status == ImportJobStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CommitRecorder : IStagingBatchCommitter
    {
        public Task CommitBatchAsync(Guid importId, IReadOnlyList<ParsedDebtRecord> records, StagingBatchCheckpoint checkpoint, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Réplica del guard de offset de <c>LocalFileSource</c>/<c>S3FileSource</c>:
    /// contenido truncado + offset de checkpoint por encima del tamaño actual.</summary>
    private sealed class ShrunkFileSource(int currentSize) : IFileSource
    {
        public Task<OpenedSource> OpenAsync(long byteOffset, CancellationToken cancellationToken)
        {
            if (byteOffset < 0 || byteOffset > currentSize)
                throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset is outside the source file.");

            var stream = new MemoryStream(new byte[currentSize], writable: false);
            stream.Seek(byteOffset, SeekOrigin.Begin);
            return Task.FromResult(new OpenedSource(stream, currentSize, Etag: null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
