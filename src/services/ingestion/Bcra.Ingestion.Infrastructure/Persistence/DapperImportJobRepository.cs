using System.Data;
using System.Data.Common;
using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Application.Ports;
using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Domain.Parsing;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;
using Npgsql;

namespace Bcra.Ingestion.Infrastructure.Persistence;

public sealed class DapperImportJobRepository(IDbConnectionFactory connectionFactory)
    : IImportJobStore, IImportStorageManager, IReferenceTableStorage, IStagingBatchCommitter
{
    private const string UniqueViolationSqlState = "23505";
    private const string SelectColumns = """
        id, source, etag, source_etag, size, periodo, status, bytes_committed, lines_processed, lines_rejected,
        lease_owner, heartbeat_at, created_at, started_at, finished_at
        """;

    /// <summary>Sin timeout de comando: las fases largas de agregación/índices corren sin límite.</summary>
    private const int IndefiniteCommandTimeout = 0;

    /// <summary><c>active_import</c> es una tabla singleton: la única fila es la id = 1.</summary>
    private const int SingletonImportRowId = 1;

    /// <summary>Lote de upsert de archivos de referencia (Maeent.txt/Nomdeu.txt).</summary>
    private const int ReferenceUpsertBatchSize = 5000;

    public async Task<(ImportJob Job, bool AlreadyExisted)> AddOrGetExistingAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            INSERT INTO import_job (id, source, etag, size, periodo, status, created_at)
            VALUES (@Id, @Source, @Etag, @Size, @Periodo, @Status, @CreatedAt)
            ON CONFLICT (source, etag, periodo) DO NOTHING;
            """,
            new
            {
                job.Id,
                job.Source,
                job.Etag,
                job.Size,
                job.Periodo,
                Status = job.Status.ToString(),
                CreatedAt = job.CreatedAtUtc
            },
            cancellationToken: cancellationToken);

        var inserted = await connection.ExecuteAsync(command);
        if (inserted > 0)
            return (job, false);

        var existing = new CommandDefinition(
            $"SELECT {SelectColumns} FROM import_job WHERE source = @Source AND etag IS NOT DISTINCT FROM @Etag AND periodo IS NOT DISTINCT FROM @Periodo LIMIT 1",
            new { job.Source, job.Etag, job.Periodo },
            cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<ImportJobRow>(existing);
        return (row.ToDomain(), true);
    }

    public async Task<ImportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM import_job WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(command);
        return row?.ToDomain();
    }

    public async Task SaveAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            UPDATE import_job SET
                etag = @Etag,
                source_etag = @SourceEtag,
                size = @Size,
                periodo = @Periodo,
                status = @Status,
                bytes_committed = @BytesCommitted,
                lines_processed = @LinesProcessed,
                lines_rejected = @LinesRejected,
                lease_owner = @LeaseOwner,
                heartbeat_at = @HeartbeatAt,
                started_at = @StartedAt,
                finished_at = @FinishedAt
            WHERE id = @Id;
            """,
            new
            {
                job.Id,
                job.Etag,
                job.SourceEtag,
                job.Size,
                job.Periodo,
                Status = job.Status.ToString(),
                job.BytesCommitted,
                job.LinesProcessed,
                job.LinesRejected,
                job.LeaseOwner,
                job.HeartbeatAt,
                StartedAt = job.StartedAtUtc,
                FinishedAt = job.FinishedAtUtc
            },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }

    public async Task<ImportJob?> ClaimNextAsync(string leaseOwner, DateTimeOffset utcNow, DateTimeOffset heartbeatCutoff, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var command = new CommandDefinition(
                """
                WITH candidate AS (
                    SELECT id
                    FROM import_job
                    WHERE status = 'Pending'
                       OR (status IN ('Loading', 'Aggregating')
                           AND (heartbeat_at IS NULL OR heartbeat_at <= @HeartbeatCutoff))
                    ORDER BY created_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                ), claimed AS (
                    UPDATE import_job
                    SET status = CASE WHEN status = 'Pending' THEN 'Loading' ELSE status END,
                        started_at = COALESCE(
                            started_at,
                            CASE WHEN status = 'Pending' THEN @Now ELSE started_at END),
                        lease_owner = @Owner,
                        heartbeat_at = @Now
                    WHERE id IN (SELECT id FROM candidate)
                    RETURNING id, source, etag, source_etag, size, periodo, status, bytes_committed, lines_processed,
                              lines_rejected, lease_owner, heartbeat_at, created_at, started_at, finished_at
                )
                SELECT * FROM claimed;
                """,
                new { Owner = leaseOwner, Now = utcNow, HeartbeatCutoff = heartbeatCutoff },
                transaction: transaction,
                cancellationToken: cancellationToken);

            var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(command);
            await transaction.CommitAsync(cancellationToken);
            return row?.ToDomain();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolationSqlState)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task<bool> RenewLeaseAsync(Guid jobId, string leaseOwner, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            UPDATE import_job
            SET heartbeat_at = @Now
            WHERE id = @Id AND lease_owner = @Owner AND status IN ('Loading', 'Aggregating');
            """,
            new { Id = jobId, Owner = leaseOwner, Now = utcNow },
            cancellationToken: cancellationToken);

        return await connection.ExecuteAsync(command) > 0;
    }

    /// <summary>
    /// COPY binario del lote + UPDATE del checkpoint (bytes_committed, líneas) + heartbeat del
    /// lease en UNA transacción READ COMMITTED: si el worker cae a mitad, no existe ventana
    /// donde un lote esté en staging sin su checkpoint (que duplicaría filas al reanudar).
    /// </summary>
    public async Task CommitBatchAsync(
        Guid importId,
        IReadOnlyList<ParsedDebtRecord> records,
        StagingBatchCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (records.Count > 0)
                await NpgsqlStagingWriter.WriteBatchAsync(connection, importId, records, cancellationToken);

            var command = new CommandDefinition(
                """
                UPDATE import_job SET
                    size = @Size,
                    source_etag = @SourceEtag,
                    bytes_committed = @BytesCommitted,
                    lines_processed = @LinesProcessed,
                    lines_rejected = @LinesRejected,
                    heartbeat_at = @HeartbeatAt
                WHERE id = @Id AND lease_owner = @LeaseOwner AND status = 'Loading';
                """,
                new
                {
                    Id = importId,
                    checkpoint.BytesCommitted,
                    checkpoint.LinesProcessed,
                    checkpoint.LinesRejected,
                    checkpoint.LeaseOwner,
                    checkpoint.HeartbeatAt,
                    checkpoint.SourceEtag,
                    checkpoint.Size
                },
                transaction: transaction,
                cancellationToken: cancellationToken);

            if (await connection.ExecuteAsync(command) == 0)
                throw new InvalidOperationException(
                    $"Lease for import '{importId}' is no longer owned by '{checkpoint.LeaseOwner}'; the batch was discarded.");

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // La conexión ya está rota: la transacción cae con ella.
            }

            throw;
        }
    }

    public async Task LoadReferenceTablesAsync(
        IAsyncEnumerable<EntidadReference> entidades,
        IAsyncEnumerable<DeudorNombreReference> nombres,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await LoadEntidadesAsync(connection, entidades, cancellationToken);
        await LoadDeudoresNombresAsync(connection, nombres, cancellationToken);
    }

    public async Task AggregateImportAsync(Guid importId, string situacionExpression, CancellationToken cancellationToken)
    {
        var staging = PartitionName("debt_staging", importId);
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var command = new CommandDefinition(
            $"""
            DELETE FROM deudor_agg WHERE import_id = @ImportId;
            DELETE FROM entidad_agg WHERE import_id = @ImportId;

            INSERT INTO deudor_agg (import_id, cuit, situacion_maxima, suma_total)
            SELECT import_id, cuit, {situacionExpression}, SUM(monto)
            FROM {staging}
            GROUP BY import_id, cuit;

            INSERT INTO entidad_agg (import_id, codigo_entidad, suma_total)
            SELECT import_id, entidad, SUM(monto)
            FROM {staging}
            GROUP BY import_id, entidad;
            """,
            new { ImportId = importId },
            commandTimeout: IndefiniteCommandTimeout,
            transaction: transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateImportStorageAsync(Guid importId, CancellationToken cancellationToken)
    {
        var partition = PartitionName("debt_staging", importId);
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"CREATE TABLE IF NOT EXISTS {partition} PARTITION OF debt_staging FOR VALUES IN ('{importId}');",
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task CreateImportIndexesAsync(Guid importId, CancellationToken cancellationToken)
    {
        var hex = Hex(importId);
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"""
            CREATE INDEX IF NOT EXISTS ix_deudor_agg_{hex}_cuit ON deudor_agg (cuit) WHERE import_id = '{importId}';
            CREATE INDEX IF NOT EXISTS ix_entidad_agg_{hex}_entidad ON entidad_agg (codigo_entidad) WHERE import_id = '{importId}';
            """,
            commandTimeout: IndefiniteCommandTimeout,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<Guid?> SwapActiveImportAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var lockCommand = new CommandDefinition(
            $"SELECT import_id FROM active_import WHERE id = {SingletonImportRowId} FOR UPDATE;",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var previous = await connection.QuerySingleOrDefaultAsync<Guid?>(lockCommand);

        var swapCommand = new CommandDefinition(
            $"UPDATE active_import SET import_id = @ImportId WHERE id = {SingletonImportRowId};",
            new { ImportId = importId },
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(swapCommand);

        await transaction.CommitAsync(cancellationToken);
        return previous;
    }

    public async Task DropImportStorageAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var activeCommand = new CommandDefinition(
            $"SELECT import_id FROM active_import WHERE id = {SingletonImportRowId};",
            cancellationToken: cancellationToken);
        var active = await connection.QuerySingleOrDefaultAsync<Guid?>(activeCommand);
        if (active == importId)
            throw new InvalidOperationException($"Refusing to drop storage of the active import '{importId}'.");

        var hex = Hex(importId);
        var staging = PartitionName("debt_staging", importId);
        var command = new CommandDefinition(
            $"""
            DROP TABLE IF EXISTS {staging};
            DROP INDEX IF EXISTS ix_deudor_agg_{hex}_cuit;
            DROP INDEX IF EXISTS ix_entidad_agg_{hex}_entidad;
            """,
            commandTimeout: IndefiniteCommandTimeout,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<ImportStats> GetImportStatsAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            SELECT
                (SELECT count(*) FROM deudor_agg WHERE import_id = @ImportId) AS total_deudores,
                (SELECT count(*) FROM entidad_agg WHERE import_id = @ImportId) AS total_entidades,
                (SELECT COALESCE(sum(suma_total), 0) FROM deudor_agg WHERE import_id = @ImportId) AS suma_total
            """,
            new { ImportId = importId },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleAsync<ImportStatsRow>(command);
        return new ImportStats(row.TotalDeudores, row.TotalEntidades, row.SumaTotal);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            "SELECT status, count(*) AS count FROM import_job GROUP BY status",
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<(string Status, int Count)>(command);
        return rows.ToDictionary(row => row.Status, row => row.Count, StringComparer.Ordinal);
    }

    private static string Hex(Guid importId) => importId.ToString("N");

    private static string PartitionName(string prefix, Guid importId) => $"{prefix}_{Hex(importId)}";

    private static async Task LoadEntidadesAsync(DbConnection connection, IAsyncEnumerable<EntidadReference> entidades, CancellationToken cancellationToken)
    {
        var codigos = new List<short>();
        var nombres = new List<string>();

        await foreach (var (codigo, nombre) in entidades.WithCancellation(cancellationToken))
        {
            codigos.Add(codigo);
            nombres.Add(nombre);

            if (codigos.Count >= ReferenceUpsertBatchSize)
            {
                await UpsertEntidadesAsync(connection, codigos, nombres, cancellationToken);
                codigos.Clear();
                nombres.Clear();
            }
        }

        if (codigos.Count > 0)
            await UpsertEntidadesAsync(connection, codigos, nombres, cancellationToken);
    }

    private static Task<int> UpsertEntidadesAsync(DbConnection connection, List<short> codigos, List<string> nombres, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            INSERT INTO entidad (codigo, nombre)
            SELECT codigo, nombre
            FROM UNNEST(@Codigos::smallint[], @Nombres::text[]) AS t(codigo, nombre)
            ON CONFLICT (codigo) DO UPDATE SET nombre = EXCLUDED.nombre;
            """,
            new { Codigos = codigos.ToArray(), Nombres = nombres.ToArray() },
            cancellationToken: cancellationToken);
        return connection.ExecuteAsync(command);
    }

    private static async Task LoadDeudoresNombresAsync(DbConnection connection, IAsyncEnumerable<DeudorNombreReference> nombresDeudores, CancellationToken cancellationToken)
    {
        var cuits = new List<string>();
        var nombres = new List<string>();

        await foreach (var (cuit, nombre) in nombresDeudores.WithCancellation(cancellationToken))
        {
            cuits.Add(cuit);
            nombres.Add(nombre);

            if (cuits.Count >= ReferenceUpsertBatchSize)
            {
                await UpsertDeudoresNombresAsync(connection, cuits, nombres, cancellationToken);
                cuits.Clear();
                nombres.Clear();
            }
        }

        if (cuits.Count > 0)
            await UpsertDeudoresNombresAsync(connection, cuits, nombres, cancellationToken);
    }

    private static Task<int> UpsertDeudoresNombresAsync(DbConnection connection, List<string> cuits, List<string> nombres, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            INSERT INTO deudor_nombre (cuit, nombre)
            SELECT cuit, nombre
            FROM UNNEST(@Cuits::char(11)[], @Nombres::text[]) AS t(cuit, nombre)
            ON CONFLICT (cuit) DO UPDATE SET nombre = EXCLUDED.nombre;
            """,
            new { Cuits = cuits.ToArray(), Nombres = nombres.ToArray() },
            cancellationToken: cancellationToken);
        return connection.ExecuteAsync(command);
    }

    private sealed class ImportStatsRow
    {
        public long TotalDeudores { get; set; }
        public long TotalEntidades { get; set; }
        public decimal SumaTotal { get; set; }
    }

    private sealed class ImportJobRow
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string? Etag { get; set; }
        public string? SourceEtag { get; set; }
        public long Size { get; set; }
        public int? Periodo { get; set; }
        public string Status { get; set; } = string.Empty;
        public long BytesCommitted { get; set; }
        public long LinesProcessed { get; set; }
        public long LinesRejected { get; set; }
        public string? LeaseOwner { get; set; }
        public DateTimeOffset? HeartbeatAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public ImportJob ToDomain() => ImportJob.Rehydrate(
            new ImportJobSnapshot(
                Id, Source, Etag, Size, Periodo,
                Enum.Parse<ImportJobStatus>(Status),
                BytesCommitted, LinesProcessed, LinesRejected,
                LeaseOwner, HeartbeatAt, CreatedAt, StartedAt, FinishedAt, SourceEtag));
    }
}
