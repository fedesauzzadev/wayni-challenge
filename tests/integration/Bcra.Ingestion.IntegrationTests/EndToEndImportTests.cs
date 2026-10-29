using System.Globalization;
using System.Text;
using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Options;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Api.Hosting;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Infrastructure.FileSources;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Situations;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bcra.Ingestion.IntegrationTests;

[Collection("postgres")]
public sealed class EndToEndImportTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Worker_ProcessesSampleFile_ToCompleted_WithStagingAggregatesAndSwap()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 1_000, rejectedLines: 3);

        var options = new IngestionOptions
        {
            BatchSize = 100,
            AllowedSourceDirs = [directory],
            LeaseTimeoutSeconds = 60,
            SituacionStrategy = "max",
            PollDelaySeconds = 1
        };
        var repository = fixture.CreateRepository();

        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);

        var worker = ImportTestHarness.CreateWorker(fixture, options);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            ImportJob? completed = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                completed = await repository.GetByIdAsync(job.Id, CancellationToken.None);
                if (completed is { Status: ImportJobStatus.Completed })
                    break;
                await Task.Delay(100, CancellationToken.None);
            }

            completed!.Status.Should().Be(ImportJobStatus.Completed);
            completed.LinesProcessed.Should().Be(sample.ValidLines);
            completed.LinesRejected.Should().Be(sample.RejectedLines);
            completed.BytesCommitted.Should().Be(sample.DeudoresLength);

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            var active = await connection.QuerySingleAsync<Guid>("SELECT import_id FROM active_import WHERE id = 1");
            active.Should().Be(job.Id, "el swap publica el import recién completado");

            var staging = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            staging.Should().Be(sample.ValidLines, "staging contiene exactamente las líneas válidas");

            var deudorRows = (await connection.QueryAsync<(string Cuit, short SituacionMaxima, decimal SumaTotal)>(
                "SELECT cuit, situacion_maxima, suma_total FROM deudor_agg WHERE import_id = @Id", new { Id = job.Id }))
                .ToDictionary(row => row.Cuit);
            deudorRows.Should().HaveCount(sample.PorCuit.Count);
            foreach (var (cuit, expected) in sample.PorCuit)
            {
                var row = deudorRows[cuit];
                row.SituacionMaxima.Should().Be(expected.SituacionMaxima, $"situacion_maxima del cuit {cuit}");
                row.SumaTotal.Should().Be(expected.Suma, $"suma_total del cuit {cuit}");
            }

            var entidadRows = (await connection.QueryAsync<(short CodigoEntidad, decimal SumaTotal)>(
                "SELECT codigo_entidad, suma_total FROM entidad_agg WHERE import_id = @Id", new { Id = job.Id }))
                .ToDictionary(row => row.CodigoEntidad);
            entidadRows.Should().HaveCount(sample.PorEntidad.Count);
            foreach (var (codigo, expectedSuma) in sample.PorEntidad)
                entidadRows[codigo].SumaTotal.Should().Be(expectedSuma, $"suma_total de la entidad {codigo}");

            var entidades = (await connection.QueryAsync<(short Codigo, string Nombre)>(
                "SELECT codigo, nombre FROM entidad ORDER BY codigo")).ToList();
            entidades.Should().HaveCount(SampleFiles.Entidades.Length);
            entidades.Should().Contain((SampleFiles.Entidades[0], "ENTIDAD 7 ÁÉÍ"));

            var nombres = await connection.QuerySingleAsync<int>("SELECT count(*) FROM deudor_nombre");
            nombres.Should().Be(sample.PorCuit.Count, "Nomdeu.txt carga un nombre por cuit del archivo");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task JobInterruptedMidLoad_WhenReclaimed_ResumesFromCheckpointWithoutDuplicatesOrLosses()
    {
        await fixture.ResetAsync();
        var directory = Path.Combine(Path.GetTempPath(), "bcra-e2e", Guid.NewGuid().ToString("N"));
        var sample = SampleFiles.Write(directory, validLines: 1_000, rejectedLines: 3);

        var options = new IngestionOptions
        {
            BatchSize = 50,
            AllowedSourceDirs = [directory],
            LeaseTimeoutSeconds = 60,
            SituacionStrategy = "max"
        };
        var repository = fixture.CreateRepository();

        var candidate = ImportJob.Create(sample.DeudoresPath, DateTimeOffset.UtcNow);
        candidate.SetSourceMetadata(sample.DeudoresLength, $"etag-{Guid.NewGuid():N}");
        candidate.SetPeriodo(202311);
        var (job, _) = await repository.AddOrGetExistingAsync(candidate, CancellationToken.None);

        var faultingWriter = new FaultingStagingWriter(new NpgsqlStagingWriter(fixture.ConnectionFactory), failOnWriteCall: 3);
        var interrupted = ImportTestHarness.CreateProcessor(fixture, options, faultingWriter);

        var aborted = await interrupted.TryProcessNextAsync("worker-1", CancellationToken.None);
        aborted.Outcome.Should().Be(ImportProcessingOutcome.AbortedByConnectionLoss);

        var stored = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(ImportJobStatus.Loading);
        stored.BytesCommitted.Should().BeGreaterThan(0);
        stored.LinesProcessed.Should().Be(100);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var committedRows = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            committedRows.Should().Be(100, "solo los lotes con checkpoint quedaron confirmados");

            await connection.ExecuteAsync(
                "UPDATE import_job SET heartbeat_at = NOW() - INTERVAL '2 hours' WHERE id = @Id", new { Id = job.Id });
        }

        var resumedProcessor = ImportTestHarness.CreateProcessor(fixture, options);
        var resumed = await resumedProcessor.TryProcessNextAsync("worker-2", CancellationToken.None);
        resumed.Outcome.Should().Be(ImportProcessingOutcome.Completed);

        var final = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        final!.LinesProcessed.Should().Be(sample.ValidLines);
        final.LinesRejected.Should().Be(sample.RejectedLines);
        final.BytesCommitted.Should().Be(sample.DeudoresLength);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            var staging = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM debt_staging WHERE import_id = @Id", new { Id = job.Id });
            staging.Should().Be(sample.ValidLines, "la reanudación no duplica ni pierde líneas");

            var sums = (await connection.QueryAsync<(string Cuit, decimal Suma)>(
                "SELECT cuit, suma_total FROM deudor_agg WHERE import_id = @Id", new { Id = job.Id }))
                .ToDictionary(row => row.Cuit);
            sums.Should().HaveCount(sample.PorCuit.Count);
            foreach (var (cuit, expected) in sample.PorCuit)
                sums[cuit].Suma.Should().Be(expected.Suma, "las sumas exactas descartan cargas duplicadas");
        }
    }

    private sealed class FaultingStagingWriter(IStagingWriter inner, int failOnWriteCall) : IStagingWriter
    {
        private int _writeCalls;

        public Task WriteBatchAsync(Guid importId, IReadOnlyList<Domain.Parsing.ParsedDebtRecord> records, CancellationToken cancellationToken)
        {
            _writeCalls++;
            if (_writeCalls == failOnWriteCall)
                throw new NpgsqlException("Simulated connection loss mid-batch");

            return inner.WriteBatchAsync(importId, records, cancellationToken);
        }
    }
}

internal static class ImportTestHarness
{
    public static ImportJobProcessor CreateProcessor(
        PostgresFixture fixture,
        IngestionOptions options,
        IStagingWriter? stagingWriter = null,
        IImportEventPublisher? eventPublisher = null)
    {
        var optionsSnapshot = Options.Create(options);
        var repository = fixture.CreateRepository();
        return new ImportJobProcessor(
            repository,
            new LocalFileSourceFactory(optionsSnapshot),
            new StagingCopyLoader(stagingWriter ?? new NpgsqlStagingWriter(fixture.ConnectionFactory), repository, TimeProvider.System, optionsSnapshot),
            new MaxLiteralSituacionStrategy(),
            eventPublisher ?? new RecordingImportEventPublisher(),
            TimeProvider.System,
            optionsSnapshot,
            NullLogger<ImportJobProcessor>.Instance);
    }

    public static ImportWorkerHostedService CreateWorker(PostgresFixture fixture, IngestionOptions options, IImportEventPublisher? eventPublisher = null)
    {
        var optionsSnapshot = Options.Create(options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<IngestionOptions>>(optionsSnapshot);
        services.AddSingleton<Domain.Situations.ISituacionAggregationStrategy>(_ => new MaxLiteralSituacionStrategy());
        services.AddSingleton<Bcra.Ingestion.Application.Ports.IFileSourceFactory>(_ => new LocalFileSourceFactory(optionsSnapshot));
        services.AddSingleton<IImportEventPublisher>(eventPublisher ?? new RecordingImportEventPublisher());
        services.AddScoped<IImportJobRepository>(_ => fixture.CreateRepository());
        services.AddSingleton<IStagingWriter>(_ => new NpgsqlStagingWriter(fixture.ConnectionFactory));
        services.AddScoped<StagingCopyLoader>();
        services.AddScoped<ImportJobProcessor>();
        var provider = services.BuildServiceProvider();
        return new ImportWorkerHostedService(
            provider,
            optionsSnapshot,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ImportWorkerHostedService>());
    }
}

internal static class SampleFiles
{
    public static readonly short[] Entidades = [7, 11, 15, 27, 40];
    private static readonly short[] Situaciones = [1, 2, 3, 4, 5, 11, 21, 23];

    public sealed record Sample(
        string Directory,
        string DeudoresPath,
        long DeudoresLength,
        int ValidLines,
        int RejectedLines,
        IReadOnlyDictionary<string, (short SituacionMaxima, decimal Suma)> PorCuit,
        IReadOnlyDictionary<short, decimal> PorEntidad);

    public static Sample Write(
        string directory,
        int validLines,
        int rejectedLines = 0,
        int seed = 20231101,
        IReadOnlyList<(string Cuit, short Entidad, short Situacion, decimal Monto)>? fixedLines = null)
    {
        Directory.CreateDirectory(directory);
        var rand = new Random(seed);
        var cuits = Enumerable.Range(0, 25).Select(i => $"2{i:D10}").ToArray();
        var porCuit = new Dictionary<string, (short SituacionMaxima, decimal Suma)>();
        var porEntidad = new Dictionary<short, decimal>();
        var content = new StringBuilder();

        void AppendValidLine(short entidad, string cuit, short situacion, decimal monto)
        {
            content.Append(BuildLine(entidad, cuit, situacion, FormatMonto(monto))).Append('\n');
            var current = porCuit.GetValueOrDefault(cuit);
            porCuit[cuit] = (Math.Max(current.SituacionMaxima, situacion), current.Suma + monto);
            porEntidad[entidad] = porEntidad.GetValueOrDefault(entidad) + monto;
        }

        foreach (var (cuit, entidad, situacion, monto) in fixedLines ?? [])
            AppendValidLine(entidad, cuit, situacion, monto);

        for (var i = 0; i < validLines; i++)
            AppendValidLine(
                Entidades[rand.Next(Entidades.Length)],
                cuits[rand.Next(cuits.Length)],
                Situaciones[rand.Next(Situaciones.Length)],
                rand.Next(0, 5_000_000) / 10m);

        for (var i = 0; i < rejectedLines; i++)
        {
            switch (i % 3)
            {
                case 0:
                    content.Append(BuildLine(7, cuits[0], 1, "1,0", tipoIdentificacion: "96")).Append('\n');
                    break;
                case 1:
                    content.Append(new string('7', 100)).Append('\n');
                    break;
                default:
                    content.Append(BuildLine(7, cuits[1], 1, "XX,0")).Append('\n');
                    break;
            }
        }

        var deudoresPath = Path.Combine(directory, "deudores.txt");
        File.WriteAllText(deudoresPath, content.ToString(), Encoding.Latin1);

        var maeent = new StringBuilder();
        foreach (var entidad in Entidades)
            maeent.Append(CultureInfo.InvariantCulture, $"{entidad:00000}{($"ENTIDAD {entidad} ÁÉÍ").PadRight(70)}").Append('\n');
        File.WriteAllText(Path.Combine(directory, "Maeent.txt"), maeent.ToString(), Encoding.Latin1);

        var nomdeu = new StringBuilder();
        foreach (var cuit in porCuit.Keys)
            nomdeu.Append(CultureInfo.InvariantCulture, $"{cuit}{($"DEUDOR {cuit}").PadRight(66)}").Append('\n');
        File.WriteAllText(Path.Combine(directory, "Nomdeu.txt"), nomdeu.ToString(), Encoding.Latin1);

        return new Sample(
            directory,
            deudoresPath,
            new FileInfo(deudoresPath).Length,
            validLines + (fixedLines?.Count ?? 0),
            rejectedLines,
            porCuit,
            porEntidad);
    }

    public static string BuildLine(
        short entidad,
        string cuit,
        short situacion,
        string montoRaw,
        string tipoIdentificacion = "11") =>
        $"{entidad:00000}202311{tipoIdentificacion}{cuit}000{situacion.ToString(CultureInfo.InvariantCulture).PadRight(2)}{montoRaw.PadRight(12)}{new string(' ', 130)}";

    private static string FormatMonto(decimal monto) =>
        monto.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
