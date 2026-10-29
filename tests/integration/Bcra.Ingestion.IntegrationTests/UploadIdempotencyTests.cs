using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.UploadImport;
using Bcra.Ingestion.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class UploadIdempotencyTests(PostgresFixture fixture)
{
    private RequestImportHandler CreateHandler() =>
        new(fixture.CreateRepository(), TimeProvider.System);

    private static RequestImportCommand Command(string source, string etag) =>
        new(source, etag, 1_000, 202311);

    [Fact]
    public async Task TwoSimultaneousUploads_OfSameFile_ProduceSingleJobAndSingleExecution()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();
        var sample = SampleFiles.Write(
            Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N")), validLines: 20);
        var command = new RequestImportCommand(sample.DeudoresPath, $"etag-{Guid.NewGuid():N}", sample.DeudoresLength, 202311);

        var results = await Task.WhenAll(
            handler.HandleAsync(command, CancellationToken.None),
            handler.HandleAsync(command, CancellationToken.None));

        results.Should().OnlyContain(r => r.IsSuccess);
        var first = results[0].Value;
        var second = results[1].Value;
        first.Id.Should().Be(second.Id);
        new[] { first.AlreadyExisted, second.AlreadyExisted }.Should().ContainSingle(e => e);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        var rows = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM import_job
            WHERE source = @Source AND etag = @Etag AND periodo = @Periodo;
            """,
            new { command.Source, command.Etag, command.Periodo });
        rows.Should().Be(1);

        var options = new IngestionOptions { BatchSize = 100, AllowedSourceDirs = [sample.Directory] };
        var processor = ImportTestHarness.CreateProcessor(fixture, options);

        var firstRun = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);
        var secondRun = await processor.TryProcessNextAsync("worker-2", CancellationToken.None);

        firstRun.Outcome.Should().Be(ImportProcessingOutcome.Completed);
        firstRun.JobId.Should().Be(first.Id);
        secondRun.Outcome.Should().Be(ImportProcessingOutcome.NothingToClaim);
    }

    [Fact]
    public async Task Upload_WithDifferentEtag_CreatesSeparateJob()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();

        var first = await handler.HandleAsync(Command("/data/multi.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);
        var second = await handler.HandleAsync(Command("/data/multi.txt", $"etag-{Guid.NewGuid():N}"), CancellationToken.None);

        first.Value.Id.Should().NotBe(second.Value.Id);
        first.Value.AlreadyExisted.Should().BeFalse();
        second.Value.AlreadyExisted.Should().BeFalse();
    }

    [Fact]
    public async Task SequentialDuplicateUpload_OfSameSourceEtagAndPeriodo_ReturnsExistingJob()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();
        var etag = $"etag-{Guid.NewGuid():N}";

        var first = await handler.HandleAsync(Command("/data/deudores.txt", etag), CancellationToken.None);
        var duplicate = await handler.HandleAsync(Command("/data/deudores.txt", etag), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Value.Id.Should().Be(first.Value.Id, "mismo source+etag+periodo devuelve el job existente");
        first.Value.AlreadyExisted.Should().BeFalse("el primero es 201 Created");
        duplicate.Value.AlreadyExisted.Should().BeTrue("el duplicado es 200 OK con el job existente");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM import_job WHERE source = @Source AND etag = @Etag AND periodo = @Periodo",
            new { Source = "/data/deudores.txt", Etag = etag, Periodo = 202311 });
        rows.Should().Be(1);
    }
}
