using Bcra.Ingestion.Application.GetLatestImport;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Shared.Domain.Errors;
using FluentAssertions;
using NSubstitute;

namespace Bcra.Ingestion.Application.Tests;

public sealed class GetLatestImportHandlerTests
{
    private readonly IImportJobStore _jobStore = Substitute.For<IImportJobStore>();
    private readonly GetLatestImportHandler _handler;

    public GetLatestImportHandlerTests()
    {
        _handler = new GetLatestImportHandler(_jobStore);
    }

    [Fact]
    public async Task WithoutImports_ReturnsNotFound()
    {
        _jobStore.GetLatestAsync(Arg.Any<CancellationToken>()).Returns((ImportJob?)null);

        var result = await _handler.HandleAsync(new GetLatestImportQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("ImportJob.NoImports");
    }

    [Fact]
    public async Task WithImports_ReturnsLatestJobSummary()
    {
        var created = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
        var started = created.AddMinutes(1);
        var finished = started.AddSeconds(90);
        var job = ImportJob.Rehydrate(new ImportJobSnapshot(
            Guid.NewGuid(),
            "s3://bcra-local/deudores.txt",
            "etag-1",
            1_000,
            202610,
            ImportJobStatus.Completed,
            BytesCommitted: 1_000,
            LinesProcessed: 9_000,
            LinesRejected: 1_000,
            LeaseOwner: null,
            HeartbeatAt: null,
            CreatedAtUtc: created,
            StartedAtUtc: started,
            FinishedAtUtc: finished));

        _jobStore.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(job);

        var result = await _handler.HandleAsync(new GetLatestImportQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var latest = result.Value;
        latest.Id.Should().Be(job.Id);
        latest.Source.Should().Be("s3://bcra-local/deudores.txt");
        latest.Status.Should().Be("Completed");
        latest.Periodo.Should().Be(202610);
        latest.Size.Should().Be(1_000);
        latest.LinesProcessed.Should().Be(9_000);
        latest.LinesRejected.Should().Be(1_000);
        latest.LoadingProgressPercent.Should().Be(100);
        latest.DurationMs.Should().Be(90_000);
        latest.CreatedAtUtc.Should().Be(created);
        latest.StartedAtUtc.Should().Be(started);
        latest.FinishedAtUtc.Should().Be(finished);
    }

    [Fact]
    public async Task RunningJob_WithoutFinishDate_HasNullDuration()
    {
        var job = ImportJob.Rehydrate(new ImportJobSnapshot(
            Guid.NewGuid(),
            "s3://bcra-local/deudores.txt",
            null,
            1_000,
            null,
            ImportJobStatus.Loading,
            BytesCommitted: 250,
            LinesProcessed: 2_500,
            LinesRejected: 0,
            LeaseOwner: "worker-1",
            HeartbeatAt: DateTimeOffset.UtcNow,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: null));

        _jobStore.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(job);

        var result = await _handler.HandleAsync(new GetLatestImportQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DurationMs.Should().BeNull();
        result.Value.LoadingProgressPercent.Should().Be(25);
        result.Value.FinishedAtUtc.Should().BeNull();
    }
}
