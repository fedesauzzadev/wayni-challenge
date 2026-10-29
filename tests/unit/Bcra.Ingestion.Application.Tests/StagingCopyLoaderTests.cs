using System.Globalization;
using System.Text;
using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Bcra.Ingestion.Application.Tests;

public sealed class StagingCopyLoaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static StagingCopyLoader CreateLoader(CommitRecorder recorder, int batchSize) =>
        new(recorder, new FixedTimeProvider(Now), Microsoft.Extensions.Options.Options.Create(new IngestionOptions { BatchSize = batchSize }), new Bcra.Shared.Infrastructure.Observability.ImportPipelineMeter());

    private static ImportJob NewLoadingJob() 
    {
        var job = ImportJob.Create("/data/deudores.txt", Now);
        job.MarkLoading(Now);
        return job;
    }

    private static ImportJob ResumedJob(long bytesCommitted, long linesProcessed, string? sourceEtag, long size)
    {
        var job = ImportJob.Rehydrate(
            Guid.NewGuid(), "/data/deudores.txt", "etag-cliente", size, 202311,
            ImportJobStatus.Loading, bytesCommitted, linesProcessed, 0, "worker-1", Now, Now, Now, null, sourceEtag);
        return job;
    }

    private static byte[] Bytes(params string[] lines) =>
        Encoding.Latin1.GetBytes(string.Join('\n', lines) + '\n');

    /// <summary>Misma disposición fija que el archivo real de deudores (validada por el parser).</summary>
    private static string ValidLine(string cuit = "20111111112", short entidad = 7, short situacion = 1, string montoRaw = "100,0") =>
        FormattableString.Invariant(
            $"{entidad:00000}20231111{cuit}000{situacion.ToString(CultureInfo.InvariantCulture).PadRight(2)}{montoRaw.PadRight(12)}{new string(' ', 130)}");

    private const string RejectedLine = "not-a-valid-record";

    [Fact]
    public async Task LoadAsync_CommitsOneCheckpointPerBatch_InSameAtomicCall()
    {
        var content = Bytes(Enumerable.Range(0, 10).Select(_ => ValidLine()).ToArray());
        var recorder = new CommitRecorder();
        var job = NewLoadingJob();

        await CreateLoader(recorder, batchSize: 4).LoadAsync(job, new FakeFileSource(content, "etag-1"), "worker-1", CancellationToken.None);

        recorder.Calls.Should().HaveCount(3, "10 líneas en lotes de 4: 4+4+2");
        recorder.Calls.Select(c => c.Records.Count).Should().ContainInOrder(4, 4, 2);
        recorder.Calls.Select(c => c.Checkpoint.LinesProcessed).Should().ContainInOrder(4, 8, 10);
        recorder.Calls.Select(c => c.Checkpoint.BytesCommitted).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
        recorder.Calls.Should().OnlyContain(c => c.ImportId == job.Id && c.Checkpoint.LeaseOwner == "worker-1" && c.Checkpoint.HeartbeatAt == Now);

        job.BytesCommitted.Should().Be(content.Length);
        job.LinesProcessed.Should().Be(10);
        job.LinesRejected.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_WithExactBatchMultiple_DoesNotEmitEmptyFinalCommit()
    {
        var content = Bytes(Enumerable.Range(0, 8).Select(_ => ValidLine()).ToArray());
        var recorder = new CommitRecorder();

        await CreateLoader(recorder, batchSize: 4).LoadAsync(NewLoadingJob(), new FakeFileSource(content, "etag-1"), "worker-1", CancellationToken.None);

        recorder.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_ResumingFromCheckpoint_OpensSourceAtOffset_AndCommitsOnlyRemainingLines()
    {
        var lines = Enumerable.Range(0, 10).Select(_ => ValidLine()).ToArray();
        var content = Bytes(lines);
        var offsetAfterSixLines = Encoding.Latin1.GetByteCount(string.Join('\n', lines[..6]) + '\n');
        var source = new FakeFileSource(content, "source-etag-1");
        var recorder = new CommitRecorder();
        var job = ResumedJob(offsetAfterSixLines, linesProcessed: 6, sourceEtag: "source-etag-1", size: content.Length);

        await CreateLoader(recorder, batchSize: 10).LoadAsync(job, source, "worker-2", CancellationToken.None);

        source.RequestedOffset.Should().Be(offsetAfterSixLines, "reanuda desde bytes_committed, no desde 0");
        recorder.Calls.Should().ContainSingle();
        var call = recorder.Calls[0];
        call.Records.Should().HaveCount(4, "solo las 4 líneas posteriores al checkpoint");
        call.Checkpoint.LinesProcessed.Should().Be(10, "6 previas + 4 nuevas");
        call.Checkpoint.BytesCommitted.Should().Be(content.Length);
        call.Checkpoint.SourceEtag.Should().Be("source-etag-1", "el source_etag persiste con el checkpoint");
        job.BytesCommitted.Should().Be(content.Length);
        job.LinesProcessed.Should().Be(10);
    }

    [Fact]
    public async Task LoadAsync_WhenResumedSourceEtagChanged_ThrowsAndNeverCommits()
    {
        var content = Bytes(ValidLine(), ValidLine());
        var recorder = new CommitRecorder();
        var job = ResumedJob(0, 0, sourceEtag: "source-etag-original", size: content.Length);

        var act = () => CreateLoader(recorder, batchSize: 10).LoadAsync(job, new FakeFileSource(content, "source-etag-distinto"), "worker-2", CancellationToken.None);

        await act.Should().ThrowAsync<SourceChangedException>();
        recorder.Calls.Should().BeEmpty();
        job.BytesCommitted.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_WhenResumedSourceSizeChanged_ThrowsAndNeverCommits()
    {
        var content = Bytes(ValidLine(), ValidLine());
        var recorder = new CommitRecorder();
        var job = ResumedJob(0, 0, sourceEtag: "source-etag-1", size: 1_000);

        var act = () => CreateLoader(recorder, batchSize: 10).LoadAsync(job, new FakeFileSource(content, "source-etag-1"), "worker-2", CancellationToken.None);

        await act.Should().ThrowAsync<SourceChangedException>();
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_FirstOpen_AdoptsSourceEtag_WithoutTouchingClientEtag()
    {
        var content = Bytes(ValidLine(), ValidLine());
        var recorder = new CommitRecorder();
        var job = NewLoadingJob();

        await CreateLoader(recorder, batchSize: 10).LoadAsync(job, new FakeFileSource(content, "source-etag-1"), "worker-1", CancellationToken.None);

        job.Etag.Should().BeNull("el etag del cliente (dedup de upload) no se sobreescribe");
        job.SourceEtag.Should().Be("source-etag-1");
        recorder.Calls[0].Checkpoint.SourceEtag.Should().Be("source-etag-1");
    }

    [Fact]
    public async Task LoadAsync_WithTrailingRejectedLines_CommitsCheckpointForEmptyBatch()
    {
        var content = Bytes(RejectedLine, RejectedLine);
        var recorder = new CommitRecorder();
        var job = ResumedJob(0, 0, sourceEtag: null, size: content.Length);

        await CreateLoader(recorder, batchSize: 4).LoadAsync(job, new FakeFileSource(content, etag: null), "worker-1", CancellationToken.None);

        recorder.Calls.Should().ContainSingle("las líneas rechazadas también avanzan el checkpoint");
        recorder.Calls[0].Records.Should().BeEmpty();
        recorder.Calls[0].Checkpoint.LinesRejected.Should().Be(2);
        recorder.Calls[0].Checkpoint.BytesCommitted.Should().Be(content.Length);
        job.LinesRejected.Should().Be(2);
    }

    [Fact]
    public async Task LoadAsync_WhenCommitterFails_PropagatesError_AndStopsLoading()
    {
        var content = Bytes(Enumerable.Range(0, 8).Select(_ => ValidLine()).ToArray());
        var recorder = new CommitRecorder(failOnCall: 2);

        var act = () => CreateLoader(recorder, batchSize: 4).LoadAsync(NewLoadingJob(), new FakeFileSource(content, "etag-1"), "worker-1", CancellationToken.None);

        await act.Should().ThrowAsync<NpgsqlSimulatedException>();
        recorder.Calls.Should().HaveCount(2, "el primer lote confirmó y el segundo abortó la carga");
    }

    private sealed class NpgsqlSimulatedException : Exception;

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeFileSource(byte[] content, string? etag) : IFileSource
    {
        public long? RequestedOffset { get; private set; }

        public Task<OpenedSource> OpenAsync(long byteOffset, CancellationToken cancellationToken)
        {
            RequestedOffset = byteOffset;
            var stream = new MemoryStream(content, writable: false);
            stream.Seek(byteOffset, SeekOrigin.Begin);
            return Task.FromResult(new OpenedSource(stream, content.Length, etag));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record CommitCall(Guid ImportId, IReadOnlyList<ParsedDebtRecord> Records, StagingBatchCheckpoint Checkpoint);

    private sealed class CommitRecorder(int? failOnCall = null) : IStagingBatchCommitter
    {
        public List<CommitCall> Calls { get; } = [];

        public Task CommitBatchAsync(Guid importId, IReadOnlyList<ParsedDebtRecord> records, StagingBatchCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            Calls.Add(new CommitCall(importId, records.ToList(), checkpoint));
            if (Calls.Count == failOnCall)
                throw new NpgsqlSimulatedException();
            return Task.CompletedTask;
        }
    }
}
