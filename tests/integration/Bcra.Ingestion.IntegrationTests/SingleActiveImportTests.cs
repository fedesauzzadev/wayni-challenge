using Bcra.Ingestion.Domain.ImportJobs;
using FluentAssertions;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class SingleActiveImportTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SecondImport_WhileOneIsActive_StaysPending_AndRunsAfterFirstCompletes()
    {
        await fixture.ResetAsync();
        var repository = fixture.CreateRepository();

        var (jobA, _) = await repository.AddOrGetExistingAsync(
            Uploads.Candidate("/data/active-a.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);
        var (jobB, _) = await repository.AddOrGetExistingAsync(
            Uploads.Candidate("/data/active-b.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(1);

        var claimedA = await repository.ClaimNextAsync("worker-1", now, cutoff, CancellationToken.None);
        claimedA!.Id.Should().Be(jobA.Id, because: "el import más antiguo se reclama primero");

        var claimedB = await repository.ClaimNextAsync("worker-2", now, cutoff, CancellationToken.None);
        claimedB.Should().BeNull(because: "la activación colisiona con el índice único parcial y no debe fallar");

        var storedB = await repository.GetByIdAsync(jobB.Id, CancellationToken.None);
        storedB!.Status.Should().Be(ImportJobStatus.Pending, "si no se puede activar, queda Pending y se reintenta");

        claimedA!.MarkAggregating();
        claimedA.MarkCompleted(DateTimeOffset.UtcNow);
        await repository.SaveAsync(claimedA, CancellationToken.None);

        var later = DateTimeOffset.UtcNow;
        var retriedB = await repository.ClaimNextAsync("worker-2", later, later - TimeSpan.FromMinutes(1), CancellationToken.None);
        retriedB!.Id.Should().Be(jobB.Id);
        retriedB.Status.Should().Be(ImportJobStatus.Loading);
    }
}
