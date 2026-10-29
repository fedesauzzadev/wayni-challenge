using Bcra.Ingestion.Domain.ImportJobs;
using Dapper;
using FluentAssertions;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class ClaimConcurrencyTests(PostgresFixture fixture)
{
    private static ImportJob NewPendingJob(string source, string etag) =>
        Uploads.Candidate(source, etag);

    [Fact]
    public async Task TwoWorkersClaimSameJob_OnlyOneWins()
    {
        await fixture.ResetAsync();
        var repository = fixture.CreateRepository();
        var job = NewPendingJob("/data/a.txt", $"etag-{Guid.NewGuid():N}");
        (job, _) = await repository.AddOrGetExistingAsync(job, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(1);

        var results = await Task.WhenAll(
            repository.ClaimNextAsync("worker-1", now, cutoff, CancellationToken.None),
            repository.ClaimNextAsync("worker-2", now, cutoff, CancellationToken.None));

        var winners = results.Where(r => r is not null).ToList();
        winners.Should().HaveCount(1);
        var winner = winners[0]!;
        winner!.Id.Should().Be(job.Id);
        winner.Status.Should().Be(ImportJobStatus.Loading);
        winner.LeaseOwner.Should().BeOneOf("worker-1", "worker-2");

        var loser = results.Single(r => r is null);
        loser.Should().BeNull();

        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Loading);
        stored.LeaseOwner.Should().Be(winner.LeaseOwner);
    }

    [Fact]
    public async Task WorkerCrash_ExpiredLease_IsReclaimableByAnotherWorker()
    {
        await fixture.ResetAsync();
        var repository = fixture.CreateRepository();
        var (job, _) = await repository.AddOrGetExistingAsync(
            NewPendingJob("/data/crash.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var claimed = await repository.ClaimNextAsync("worker-1", now, now - TimeSpan.FromMinutes(1), CancellationToken.None);
        claimed!.Id.Should().Be(job.Id);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE import_job SET heartbeat_at = heartbeat_at - interval '10 minutes' WHERE id = @Id",
            new { Id = job.Id });

        var later = DateTimeOffset.UtcNow;
        var reclaimed = await repository.ClaimNextAsync(
            "worker-2", later, later - TimeSpan.FromMinutes(1), CancellationToken.None);

        reclaimed!.Id.Should().Be(job.Id);
        reclaimed.LeaseOwner.Should().Be("worker-2");
        reclaimed.Status.Should().Be(ImportJobStatus.Loading);
        reclaimed.HeartbeatAt.Should().BeCloseTo(later, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task RenewLease_ByNonOwner_ReturnsFalse()
    {
        await fixture.ResetAsync();
        var repository = fixture.CreateRepository();
        var (job, _) = await repository.AddOrGetExistingAsync(
            NewPendingJob("/data/renew.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        await repository.ClaimNextAsync("worker-1", now, now, CancellationToken.None);

        var renewed = await repository.RenewLeaseAsync(job.Id, "worker-2", DateTimeOffset.UtcNow, CancellationToken.None);

        renewed.Should().BeFalse();
    }
}

internal static class Uploads
{
    public static ImportJob Candidate(string source, string etag)
    {
        var job = ImportJob.Create(source, DateTimeOffset.UtcNow);
        job.SetSourceMetadata(1_000, etag);
        job.SetPeriodo(202311);
        return job;
    }
}
