using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Situations;
using Dapper;
using FluentAssertions;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

/// <summary>
/// Reanudaci贸n e idempotencia: un import interrumpido (worker ca铆do en Loading o Aggregating)
/// es reclamado tras vencer el lease y completa sin duplicar filas ni inflar sumas.
/// </summary>
[Collection("postgres")]
public sealed class ResumptionTests(PostgresFixture fixture)
{
    private DapperImportJobRepository Repository => fixture.CreateRepository();

    private static IngestionOptions Options(string directory, int batchSize) => new()
    {
        BatchSize = batchSize,
        AllowedSourceDirs = [directory],
        SituacionStrategy = "max"
    };

    private static async Task<ImportJob> QueueJobAsync(SampleFiles.Sample sample, DapperImportJobRepository repository)
    {
        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);
        return job;
    }

    private static async Task ExpireLeaseAsync(PostgresFixture fixture, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE import_job SET heartbeat_at = NOW() - INTERVAL '2 hours' WHERE id = @Id",
            new { Id = jobId });
    }

    [Fact]
    public async Task LoadingInterrupted_TakeoverAfterExpiredLease_CompletesWithIdenticalAggregates()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-resume", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 300, rejectedLines: 3);
        var options = Options(directory, batchSize: 50);
        var repository = Repository;
        var job = await QueueJobAsync(sample, repository);

        var interrupted = ImportTestHarness.CreateProcessor(
            fixture, options, batchCommitter: new FaultingStagingBatchCommitter(repository, failOnCommitCall: 3));
        var aborted = await interrupted.TryProcessNextAsync("worker-1", CancellationToken.None);

        aborted.Outcome.Should().Be(ImportProcessingOutcome.AbortedByConnectionLoss);
        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Loading);
        stored.BytesCommitted.Should().BeGreaterThan(0);
        stored.LinesProcessed.Should().Be(100, "dos lotes de 50 confirmados antes de la ca铆da");

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var committedRows = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            committedRows.Should().Be(100, "el lote fallido no dej贸 filas hu茅rfanas: COPY y checkpoint son at贸micos");
        }

        await ExpireLeaseAsync(fixture, job.Id);

        var takeover = ImportTestHarness.CreateProcessor(fixture, options);
        var resumed = await takeover.TryProcessNextAsync("worker-2", CancellationToken.None);

        resumed.Outcome.Should().Be(ImportProcessingOutcome.Completed);
        resumed.JobId.Should().Be(job.Id);

        var final = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        final!.Status.Should().Be(ImportJobStatus.Completed);
        final.LinesProcessed.Should().Be(sample.ValidLines);
        final.LinesRejected.Should().Be(sample.RejectedLines);
        final.LinesSkippedControl.Should().Be(sample.SkippedControlLines, "las de control descartadas tambi閚 sobreviven la reanudaci髇");
        final.BytesCommitted.Should().Be(sample.DeudoresLength);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var staging = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            staging.Should().Be(sample.ValidLines, "ni filas duplicadas ni perdidas tras el takeover");

            await AssertAggregatesExactAsync(connection, job.Id, sample);
        }
    }

    [Fact]
    public async Task AggregatingTakeover_ReexecutesAggregation_Idempotently()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-resume", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 200);
        var options = Options(directory, batchSize: 100);
        var repository = Repository;
        var job = await QueueJobAsync(sample, repository);

        var interrupted = ImportTestHarness.CreateProcessor(
            fixture, options, situacionStrategy: new FaultingSituacionStrategy(new MaxLiteralSituacionStrategy(), failOnCall: 1));
        var aborted = await interrupted.TryProcessNextAsync("worker-1", CancellationToken.None);

        aborted.Outcome.Should().Be(ImportProcessingOutcome.AbortedByConnectionLoss);
        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Aggregating, "la carga complet贸 y la ca铆da fue en la agregaci贸n");
        stored.BytesCommitted.Should().Be(sample.DeudoresLength);

        // Agregaci贸n parcial/stale previo al takeover (simula un intento que complet贸 el INSERT
        // pero muri贸 antes de marcar Completed): el DELETE por import_id la debe reemplazar.
        await repository.AggregateImportAsync(job.Id, "MAX(situacion)", CancellationToken.None);
        await repository.AggregateImportAsync(job.Id, "MAX(situacion)", CancellationToken.None);

        await ExpireLeaseAsync(fixture, job.Id);

        var takeover = ImportTestHarness.CreateProcessor(fixture, options);
        var resumed = await takeover.TryProcessNextAsync("worker-2", CancellationToken.None);

        resumed.Outcome.Should().Be(ImportProcessingOutcome.Completed);
        var final = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        final!.Status.Should().Be(ImportJobStatus.Completed);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await AssertAggregatesExactAsync(connection, job.Id, sample);
        }
    }

    [Fact]
    public async Task SourceChangedOnResume_MarksJobFailed_InsteadOfInfiniteRetry()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-resume", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 200);
        var options = Options(directory, batchSize: 50);
        var repository = Repository;
        var job = await QueueJobAsync(sample, repository);

        var interrupted = ImportTestHarness.CreateProcessor(
            fixture, options, batchCommitter: new FaultingStagingBatchCommitter(repository, failOnCommitCall: 3));
        var aborted = await interrupted.TryProcessNextAsync("worker-1", CancellationToken.None);
        aborted.Outcome.Should().Be(ImportProcessingOutcome.AbortedByConnectionLoss);

        // La fuente cambi贸 mientras el job estaba interrumpido (size y etag mtime cambian).
        await File.AppendAllTextAsync(
            sample.DeudoresPath,
            SampleFiles.BuildLine(7, "20999999999", 1, "500,0") + Environment.NewLine,
            System.Text.Encoding.Latin1);

        await ExpireLeaseAsync(fixture, job.Id);

        var takeover = ImportTestHarness.CreateProcessor(fixture, options);
        var resumed = await takeover.TryProcessNextAsync("worker-2", CancellationToken.None);

        resumed.Outcome.Should().Be(ImportProcessingOutcome.Failed);
        resumed.JobId.Should().Be(job.Id);

        var final = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        final!.Status.Should().Be(ImportJobStatus.Failed);
        final.IsTerminal.Should().BeTrue();

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var staging = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            staging.Should().Be(100, "no se carg贸 nada nuevo desde la fuente modificada");
        }

        var reclaimed = await takeover.TryProcessNextAsync("worker-3", CancellationToken.None);
        reclaimed.Outcome.Should().Be(ImportProcessingOutcome.NothingToClaim, "Failed es terminal: no hay retry infinito con lease vencido");
    }

    [Fact]
    public async Task SourceDeletedBeforeClaim_MarksJobFailed_InsteadOfInfiniteRetry()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-resume", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 50);
        var options = Options(directory, batchSize: 50);
        var repository = Repository;
        var job = await QueueJobAsync(sample, repository);

        File.Delete(sample.DeudoresPath);

        var processor = ImportTestHarness.CreateProcessor(fixture, options);
        var result = await processor.TryProcessNextAsync("worker-1", CancellationToken.None);

        result.Outcome.Should().Be(ImportProcessingOutcome.Failed);
        result.JobId.Should().Be(job.Id);

        var final = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        final!.Status.Should().Be(ImportJobStatus.Failed);
        final.IsTerminal.Should().BeTrue();
        final.BytesCommitted.Should().Be(0);

        var reclaimed = await processor.TryProcessNextAsync("worker-2", CancellationToken.None);
        reclaimed.Outcome.Should().Be(ImportProcessingOutcome.NothingToClaim, "Failed es terminal: la fuente borrada no bloquea la cola con reclaims infinitos");
    }

    private static async Task AssertAggregatesExactAsync(NpgsqlConnection connection, Guid jobId, SampleFiles.Sample sample)
    {
        var deudores = (await connection.QueryAsync<(string Cuit, decimal Suma)>(
            "SELECT cuit, suma_total FROM deudor_agg WHERE import_id = @Id", new { Id = jobId }))
            .ToDictionary(row => row.Cuit);
        deudores.Should().HaveCount(sample.PorCuit.Count);
        foreach (var (cuit, expected) in sample.PorCuit)
            deudores[cuit].Suma.Should().Be(expected.Suma, $"suma_total exacta del cuit {cuit} (sin inflar por re-ejecuci贸n)");

        var entidades = (await connection.QueryAsync<(int Codigo, decimal Suma)>(
            "SELECT codigo_entidad, suma_total FROM entidad_agg WHERE import_id = @Id", new { Id = jobId }))
            .ToDictionary(row => row.Codigo);
        entidades.Should().HaveCount(sample.PorEntidad.Count);
        foreach (var (codigo, expectedSuma) in sample.PorEntidad)
            entidades[codigo].Suma.Should().Be(expectedSuma, $"suma_total exacta de la entidad {codigo}");
    }

    private sealed class FaultingSituacionStrategy(ISituacionAggregationStrategy inner, int failOnCall) : ISituacionAggregationStrategy
    {
        private int _calls;

        public string Name => inner.Name;

        public string BuildAggregationExpression(string column = "situacion")
        {
            _calls++;
            if (_calls == failOnCall)
                throw new NpgsqlException("Simulated connection loss before aggregation");
            return inner.BuildAggregationExpression(column);
        }
    }
}
