using Bcra.Ingestion.Domain.ImportJobs;
using FluentAssertions;
using Xunit;

namespace Bcra.Ingestion.Domain.Tests;

public class ImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ImportJob NewJob() => ImportJob.Create("/data/deudores.txt", Now);

    [Fact]
    public void Create_StartsPendingWithZeroProgress()
    {
        var job = NewJob();

        job.Status.Should().Be(ImportJobStatus.Pending);
        job.BytesCommitted.Should().Be(0);
        job.LinesProcessed.Should().Be(0);
        job.LinesRejected.Should().Be(0);
        job.Periodo.Should().BeNull();
        job.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void MarkLoading_SetsStartedAt()
    {
        var job = NewJob();

        job.MarkLoading(Now.AddMinutes(1));

        job.Status.Should().Be(ImportJobStatus.Loading);
        job.StartedAtUtc.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void MarkAggregating_FromPending_Throws()
    {
        var job = NewJob();
        var act = () => job.MarkAggregating();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void HappyPath_PendingToCompleted()
    {
        var job = NewJob();
        job.MarkLoading(Now);
        job.MarkAggregating();
        job.MarkCompleted(Now.AddMinutes(10));

        job.Status.Should().Be(ImportJobStatus.Completed);
        job.FinishedAtUtc.Should().Be(Now.AddMinutes(10));
        job.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(ImportJobStatus.Completed)]
    [InlineData(ImportJobStatus.Failed)]
    public void TerminalStatuses_AreFinal(ImportJobStatus terminal)
    {
        var job = NewJob();
        job.MarkLoading(Now);
        job.MarkAggregating();
        if (terminal == ImportJobStatus.Completed)
        {
            job.MarkCompleted(Now);
        }
        else
        {
            job.MarkFailed(Now);
        }

        var act = () => job.MarkAggregating();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkFailed_FromLoading_Succeeds()
    {
        var job = NewJob();
        job.MarkLoading(Now);

        job.MarkFailed(Now.AddMinutes(5));

        job.Status.Should().Be(ImportJobStatus.Failed);
        job.FinishedAtUtc.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void CommitBatch_AccumulatesLinesAndAdvancesBytes()
    {
        var job = NewJob();
        job.MarkLoading(Now);
        job.SetSourceMetadata(1_000, "etag-1");

        job.CommitBatch(500, 200_000, 12);
        job.CommitBatch(900, 150_000, 3);

        job.BytesCommitted.Should().Be(900);
        job.LinesProcessed.Should().Be(350_000);
        job.LinesRejected.Should().Be(15);
    }

    [Fact]
    public void CommitBatch_MovingBackwards_Throws()
    {
        var job = NewJob();
        job.MarkLoading(Now);
        job.SetSourceMetadata(1_000, null);
        job.CommitBatch(500, 1, 0);

        var act = () => job.CommitBatch(400, 1, 0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CommitBatch_WhenNotLoading_Throws()
    {
        var job = NewJob();

        var act = () => job.CommitBatch(100, 1, 0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetSourceMetadata_DetectsSizeChange()
    {
        var job = NewJob();
        job.SetSourceMetadata(1_000, "etag-1");

        var act = () => job.SetSourceMetadata(2_000, "etag-1");

        act.Should().Throw<InvalidOperationException>().WithMessage("*size*");
    }

    [Fact]
    public void SetSourceMetadata_DetectesEtagChange()
    {
        var job = NewJob();
        job.SetSourceMetadata(1_000, "etag-1");

        var act = () => job.SetSourceMetadata(1_000, "etag-2");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ETag*");
    }

    [Fact]
    public void SetPeriodo_WithDifferentPeriod_Throws()
    {
        var job = NewJob();
        job.SetPeriodo(202311);

        var act = () => job.SetPeriodo(202312);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Mixed periods*");
    }

    [Fact]
    public void SetPeriodo_WithSamePeriod_IsIdempotent()
    {
        var job = NewJob();
        job.SetPeriodo(202311);
        job.SetPeriodo(202311);

        job.Periodo.Should().Be(202311);
    }

    [Fact]
    public void Rehydrate_PreservesAllFields()
    {
        var job = ImportJob.Rehydrate(
            Guid.NewGuid(), "/data/x.txt", "etag", 100, 202311,
            ImportJobStatus.Loading, 60, 10, 2, "worker-1", Now, Now, Now, null);

        job.Id.Should().NotBeEmpty();
        job.Status.Should().Be(ImportJobStatus.Loading);
        job.BytesCommitted.Should().Be(60);
        job.LeaseOwner.Should().Be("worker-1");
    }

    private sealed class FixedTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utcNow = initial;

        public void Advance(TimeSpan delta) => _utcNow += delta;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public void Claim_FromPending_TransitionsToLoadingAndTakesLease()
    {
        var job = NewJob();

        job.Claim("worker-1", Now, LeaseTimeout);

        job.Status.Should().Be(ImportJobStatus.Loading);
        job.LeaseOwner.Should().Be("worker-1");
        job.HeartbeatAt.Should().Be(Now);
        job.StartedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Claim_FromPending_WithInjectableClock_DecidesViaTimeProvider()
    {
        var clock = new FixedTimeProvider(Now);
        var job = NewJob();

        job.IsClaimable(clock, LeaseTimeout).Should().BeTrue();
        job.Claim("worker-1", clock.GetUtcNow(), LeaseTimeout);

        clock.Advance(LeaseTimeout + TimeSpan.FromSeconds(1));
        job.HasExpiredLease(clock.GetUtcNow(), LeaseTimeout).Should().BeTrue();
    }

    [Fact]
    public void Claim_ExpiredLeaseOnActiveJob_TakesOverLease()
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        var takeoverAt = Now + LeaseTimeout + TimeSpan.FromSeconds(1);
        job.Claim("worker-2", takeoverAt, LeaseTimeout);

        job.Status.Should().Be(ImportJobStatus.Loading);
        job.LeaseOwner.Should().Be("worker-2");
        job.HeartbeatAt.Should().Be(takeoverAt);
        job.StartedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Claim_WithFreshLease_Throws()
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        var act = () => job.Claim("worker-2", Now + TimeSpan.FromSeconds(10), LeaseTimeout);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be claimed*");
    }

    [Fact]
    public void Claim_OnTerminalJob_Throws()
    {
        var job = NewJob();
        job.MarkLoading(Now);
        job.MarkAggregating();
        job.MarkCompleted(Now);

        var act = () => job.Claim("worker-2", Now, LeaseTimeout);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    public void HasExpiredLease_Boundary_DecisionByElapsedSeconds(int elapsedSeconds, bool expectedExpired)
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        job.HasExpiredLease(Now + TimeSpan.FromSeconds(elapsedSeconds), LeaseTimeout).Should().Be(expectedExpired);
    }

    [Fact]
    public void IsClaimable_ActiveJobWithNullHeartbeat_IsClaimable()
    {
        var job = ImportJob.Rehydrate(
            Guid.NewGuid(), "/data/x.txt", "etag", 100, 202311,
            ImportJobStatus.Aggregating, 100, 10, 0, "worker-1", null, Now, Now, null);

        job.IsClaimable(Now, LeaseTimeout).Should().BeTrue();
    }

    [Fact]
    public void RenewLease_ByOwner_UpdatesHeartbeat()
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        var renewedAt = Now + TimeSpan.FromSeconds(30);
        job.RenewLease("worker-1", renewedAt);

        job.HeartbeatAt.Should().Be(renewedAt);
        job.HasExpiredLease(renewedAt + TimeSpan.FromSeconds(59), LeaseTimeout).Should().BeFalse();
    }

    [Fact]
    public void RenewLease_ByOtherWorker_Throws()
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        var act = () => job.RenewLease("worker-2", Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*owned by*");
    }

    [Fact]
    public void ReleaseClaim_ClearsLease_KeepingNonTerminalStatus()
    {
        var job = NewJob();
        job.Claim("worker-1", Now, LeaseTimeout);

        job.ReleaseClaim();

        job.LeaseOwner.Should().BeNull();
        job.HeartbeatAt.Should().BeNull();
        job.Status.Should().Be(ImportJobStatus.Loading);
    }
}
