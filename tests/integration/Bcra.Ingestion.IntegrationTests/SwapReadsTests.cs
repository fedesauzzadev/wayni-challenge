using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.UploadImport;
using Bcra.Ingestion.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class SwapReadsTests(PostgresFixture fixture)
{
    private const string Cuit = "30500000000";

    [Fact]
    public async Task DuringSwap_ConcurrentReaders_SeeOldOrNewVersion_NeverMixedOrError()
    {
        await fixture.ResetAsync();
        var repository = fixture.CreateRepository();
        var processor = new ImportJobProcessor(repository, TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new IngestionOptions()));
        var uploadHandler = new RequestImportHandler(repository, TimeProvider.System);

        var importA = (await uploadHandler.HandleAsync(
            new RequestImportCommand("/data/swap-a.txt", $"etag-{Guid.NewGuid():N}", 1_000, 202311),
            CancellationToken.None)).Value;
        var runA = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);
        runA.Outcome.Should().Be(ImportProcessingOutcome.Completed);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO deudor_agg (import_id, cuit, situacion_maxima, suma_total) VALUES (@Id, @Cuit, 1, 100.0)",
                new { Id = importA.Id, Cuit = Cuit });
        }

        var importB = (await uploadHandler.HandleAsync(
            new RequestImportCommand("/data/swap-b.txt", $"etag-{Guid.NewGuid():N}", 1_000, 202311),
            CancellationToken.None)).Value;
        await repository.CreateImportStorageAsync(importB.Id, CancellationToken.None);
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO deudor_agg (import_id, cuit, situacion_maxima, suma_total) VALUES (@Id, @Cuit, 1, 200.0)",
                new { Id = importB.Id, Cuit = Cuit });
        }

        var processing = Task.Run(() => processor.TryProcessNextAsync("worker-2", CancellationToken.None));
        var observations = new List<(Guid ImportId, decimal Suma)>();
        var errors = new List<Exception>();

        while (!processing.IsCompleted)
        {
            try
            {
                await using var connection = new NpgsqlConnection(fixture.ConnectionString);
                var row = await connection.QuerySingleOrDefaultAsync<(Guid, decimal)>(
                    """
                    SELECT a.import_id, d.suma_total
                    FROM active_import a
                    JOIN deudor_agg d ON d.import_id = a.import_id
                    WHERE d.cuit = @Cuit;
                    """,
                    new { Cuit = Cuit });
                if (row != default)
                    observations.Add(row);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        var result = await processing;
        result.Outcome.Should().Be(ImportProcessingOutcome.Completed);

        errors.Should().BeEmpty("las lecturas concurrentes nunca deben fallar durante el swap");
        observations.Should().NotBeEmpty();
        observations.Should().OnlyContain(o =>
            (o.Item1 == importA.Id && o.Item2 == 100.0m) || (o.Item1 == importB.Id && o.Item2 == 200.0m),
            "cada lectura ve la versión vieja o la nueva completa, nunca mixta");

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var active = await connection.QuerySingleAsync<Guid>("SELECT import_id FROM active_import WHERE id = 1");
            active.Should().Be(importB.Id);

            var stagingA = await connection.QuerySingleOrDefaultAsync<string>(
                "SELECT to_regclass(@Name)::text", new { Name = $"debt_staging_{importA.Id:N}" });
            stagingA.Should().BeNull("la partición del import anterior se limpia con DROP tras el swap");

            var stagingB = await connection.QuerySingleOrDefaultAsync<string>(
                "SELECT to_regclass(@Name)::text", new { Name = $"debt_staging_{importB.Id:N}" });
            stagingB.Should().NotBeNull("nunca se dropea el almacenamiento del import activo");
        }
    }
}
