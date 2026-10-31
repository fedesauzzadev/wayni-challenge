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

    [Fact]
    public async Task DuplicateUpload_OfCompletedImportSupersededByLaterImport_StartsNewRunAndRepublishes()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();

        var directoryA = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var directoryB = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var sampleA = SampleFiles.Write(directoryA, validLines: 60);
        var sampleB = SampleFiles.Write(directoryB, validLines: 40, seed: 20241215);

        var options = new IngestionOptions
        {
            BatchSize = 50,
            AllowedSourceDirs = [directoryA, directoryB],
            LeaseTimeoutSeconds = 60,
            SituacionStrategy = "max"
        };
        var processor = ImportTestHarness.CreateProcessor(fixture, options);

        var commandA = new RequestImportCommand(sampleA.DeudoresPath, "etag-superseded", sampleA.DeudoresLength, 202311);
        var commandB = new RequestImportCommand(sampleB.DeudoresPath, "etag-superseder", sampleB.DeudoresLength, 202311);
        var firstRun = await handler.HandleAsync(commandA, CancellationToken.None);
        await handler.HandleAsync(commandB, CancellationToken.None);
        firstRun.IsSuccess.Should().BeTrue();

        (await processor.TryProcessNextAsync("worker-1", CancellationToken.None)).Outcome.Should().Be(ImportProcessingOutcome.Completed);
        (await processor.TryProcessNextAsync("worker-2", CancellationToken.None)).Outcome.Should().Be(ImportProcessingOutcome.Completed);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var active = await connection.QuerySingleAsync<Guid>("SELECT import_id FROM active_import WHERE id = 1");
            active.Should().NotBe(firstRun.Value.Id, "el import B desplazó al A del puntero activo");
            var aggregateRows = await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM deudor_agg WHERE import_id = @Id", new { Id = firstRun.Value.Id });
            aggregateRows.Should().Be(0, "el swap del import B limpió los agregados del A: A es un Completed zombie");
        }

        var duplicate = await handler.HandleAsync(commandA, CancellationToken.None);

        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Value.AlreadyExisted.Should().BeFalse("el Completed zombie se descarta y se arranca una corrida nueva");
        duplicate.Value.Status.Should().Be("Pending");
        duplicate.Value.Id.Should().NotBe(firstRun.Value.Id);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var rows = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM import_job WHERE source = @Source AND etag = @Etag AND periodo = @Periodo",
                new { commandA.Source, commandA.Etag, commandA.Periodo });
            rows.Should().Be(1, "la fila del zombie fue reemplazada, no acumulada");
            var oldRow = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT id FROM import_job WHERE id = @Id", new { Id = firstRun.Value.Id });
            oldRow.Should().BeNull();
        }

        (await processor.TryProcessNextAsync("worker-3", CancellationToken.None)).Outcome.Should().Be(ImportProcessingOutcome.Completed);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var active = await connection.QuerySingleAsync<Guid>("SELECT import_id FROM active_import WHERE id = 1");
            active.Should().Be(duplicate.Value.Id, "la corrida nueva vuelve a publicar los datos del archivo");
            var aggregateRows = await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM deudor_agg WHERE import_id = @Id", new { Id = duplicate.Value.Id });
            aggregateRows.Should().Be(sampleA.PorCuit.Count);
        }
    }

    [Fact]
    public async Task DuplicateUpload_OfPublishedCompletedImport_ReturnsSameJobWithoutReimporting()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();
        var sample = SampleFiles.Write(
            Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N")), validLines: 30);
        var command = new RequestImportCommand(sample.DeudoresPath, $"etag-{Guid.NewGuid():N}", sample.DeudoresLength, 202311);

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var options = new IngestionOptions { BatchSize = 50, AllowedSourceDirs = [sample.Directory] };
        (await ImportTestHarness.CreateProcessor(fixture, options).TryProcessNextAsync("worker-1", CancellationToken.None))
            .Outcome.Should().Be(ImportProcessingOutcome.Completed);

        var duplicate = await handler.HandleAsync(command, CancellationToken.None);

        duplicate.Value.Id.Should().Be(first.Value.Id, "el import publicado se devuelve tal cual: idempotencia real");
        duplicate.Value.AlreadyExisted.Should().BeTrue();
        duplicate.Value.Status.Should().Be("Completed");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM import_job WHERE source = @Source AND etag = @Etag AND periodo = @Periodo",
            new { command.Source, command.Etag, command.Periodo });
        rows.Should().Be(1);
        var aggregateRows = await connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM deudor_agg WHERE import_id = @Id", new { Id = first.Value.Id });
        aggregateRows.Should().Be(sample.PorCuit.Count, "no se re-procesa ni se pierde el import publicado");
    }

    [Fact]
    public async Task DuplicateUpload_OfFailedImport_DiscardsResidualStorageAndStartsNewRun()
    {
        await fixture.ResetAsync();
        var handler = CreateHandler();
        var sample = SampleFiles.Write(
            Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N")), validLines: 30);
        var command = new RequestImportCommand(sample.DeudoresPath, $"etag-{Guid.NewGuid():N}", sample.DeudoresLength, 202311);

        var first = await handler.HandleAsync(command, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Corrida que terminó Failed con storage residual: partición de staging con filas del checkpoint.
        await fixture.CreateRepository().CreateImportStorageAsync(first.Value.Id, CancellationToken.None);
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO debt_staging (import_id, entidad, cuit, situacion, monto) VALUES (@Id, 7, '20123456789', 1, 100.0)",
                new { Id = first.Value.Id });
            await connection.ExecuteAsync(
                "UPDATE import_job SET status = 'Failed', finished_at = NOW() WHERE id = @Id", new { Id = first.Value.Id });
        }

        var duplicate = await handler.HandleAsync(command, CancellationToken.None);

        duplicate.Value.Id.Should().NotBe(first.Value.Id, "el Failed se descarta y se arranca una corrida nueva");
        duplicate.Value.Status.Should().Be("Pending");
        duplicate.Value.AlreadyExisted.Should().BeFalse();

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var rows = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM import_job WHERE source = @Source AND etag = @Etag AND periodo = @Periodo",
                new { command.Source, command.Etag, command.Periodo });
            rows.Should().Be(1);
            var partition = await connection.QuerySingleOrDefaultAsync<string?>(
                "SELECT to_regclass(@Name)::text", new { Name = $"debt_staging_{first.Value.Id:N}" });
            partition.Should().BeNull("la partición residual del Failed se limpia con el reemplazo");
        }
    }
}
