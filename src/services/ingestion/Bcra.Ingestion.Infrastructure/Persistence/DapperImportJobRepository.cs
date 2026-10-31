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
        lines_skipped_control, lease_owner, heartbeat_at, created_at, started_at, finished_at
        """;

    /// <summary>Sin timeout de comando: las fases largas de agregación/índices corren sin límite.</summary>
    private const int IndefiniteCommandTimeout = 0;

    /// <summary><c>active_import</c> es una tabla singleton: la única fila es la id = 1.</summary>
    private const int SingletonImportRowId = 1;

    /// <summary>Lote de upsert de archivos de referencia (Maeent.txt/Nomdeu.txt).</summary>
    private const int ReferenceUpsertBatchSize = 5000;

    /// <summary>
    /// work_mem de la sesión de agregación: el GROUP BY de ~20M grupos distintos con el default
    /// de 4MB spillea a disco (la tabla hash completa necesita ~2-3GB). Session-scoped (Npgsql
    /// resetea la sesión al devolver la conexión al pool) y consumido por un único nodo de
    /// agregación, por lo que la demanda real acota a ~1× este valor — ver ADR-011.
    /// </summary>
    private const string AggregationWorkMem = "2GB";

    /// <summary>
    /// maintenance_work_mem de la sesión de creación de índices: el sort de los CREATE INDEX con
    /// el default de 64MB es externo (disco). Session-scoped, mismo criterio que
    /// <see cref="AggregationWorkMem"/> — ver ADR-011.
    /// </summary>
    private const string IndexMaintenanceWorkMem = "512MB";

    /// <summary>
    /// Intento máximo de resolución de un upload duplicado: la fila existente puede desaparecer
    /// entre el INSERT y el SELECT FOR UPDATE cuando otro duplicado concurrente reemplaza un
    /// job terminal; el reintento del INSERT colisiona entonces con la fila nueva y la devuelve.
    /// </summary>
    private const int MaxDuplicateResolutionAttempts = 3;

    public async Task<(ImportJob Job, bool AlreadyExisted)> AddOrGetExistingAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            if (await InsertJobAsync(connection, job, transaction: null, cancellationToken) > 0)
                return (job, false);

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var select = new CommandDefinition(
                    $"""
                    SELECT {SelectColumns} FROM import_job
                    WHERE source = @Source AND etag IS NOT DISTINCT FROM @Etag AND periodo IS NOT DISTINCT FROM @Periodo
                    FOR UPDATE;
                    """,
                    new { job.Source, job.Etag, job.Periodo },
                    transaction: transaction,
                    cancellationToken: cancellationToken);
                var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(select);

                if (row is null)
                {
                    if (attempt >= MaxDuplicateResolutionAttempts)
                        throw new InvalidOperationException(
                            $"Could not resolve a duplicate import request for source '{job.Source}' after {MaxDuplicateResolutionAttempts} attempts.");

                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                if (await IsReusableAsync(connection, transaction, row, cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return (row.ToDomain(), true);
                }

                await ReplaceTerminalJobAsync(connection, transaction, row.Id, job, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (job, false);
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
    }

    private static Task<int> InsertJobAsync(DbConnection connection, ImportJob job, DbTransaction? transaction, CancellationToken cancellationToken)
    {
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
            transaction: transaction,
            cancellationToken: cancellationToken);

        return connection.ExecuteAsync(command);
    }

    /// <summary>
    /// El job existente se reutiliza (idempotencia del upload) si está en curso o si es el
    /// import publicado en <c>active_import</c>: en ambos casos el duplicado es un no-op.
    /// Un terminal NO publicado se descarta: el Completed cuyos agregados fueron limpiados
    /// por el swap de un import posterior (sus datos ya no existen) y el Failed definitivo.
    /// </summary>
    private static async Task<bool> IsReusableAsync(DbConnection connection, DbTransaction transaction, ImportJobRow row, CancellationToken cancellationToken)
    {
        var status = Enum.Parse<ImportJobStatus>(row.Status);
        if (status is not (ImportJobStatus.Completed or ImportJobStatus.Failed))
            return true;

        if (status == ImportJobStatus.Failed)
            return false;

        var activeCommand = new CommandDefinition(
            $"SELECT import_id FROM active_import WHERE id = {SingletonImportRowId};",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var activeId = await connection.QuerySingleOrDefaultAsync<Guid?>(activeCommand);
        return activeId == row.Id;
    }

    /// <summary>
    /// Descarta el job terminal no publicado dentro de la transacción que tiene su fila
    /// bloqueada: limpia el storage residual (partición de staging, índices parciales y
    /// agregados que un cleanup incompleto pudo dejar) y borra la fila, liberando el tuple
    /// (source, etag, periodo) para la corrida nueva que se inserta a continuación.
    /// </summary>
    private static async Task ReplaceTerminalJobAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid doomedJobId,
        ImportJob replacement,
        CancellationToken cancellationToken)
    {
        var cleanup = new CommandDefinition(
            BuildImportStorageCleanupSql(doomedJobId),
            new { ImportId = doomedJobId },
            commandTimeout: IndefiniteCommandTimeout,
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(cleanup);

        var delete = new CommandDefinition(
            "DELETE FROM import_job WHERE id = @Id;",
            new { Id = doomedJobId },
            transaction: transaction,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(delete);

        if (await InsertJobAsync(connection, replacement, transaction, cancellationToken) == 0)
            throw new InvalidOperationException(
                $"Failed to insert the replacement import for source '{replacement.Source}'; resolve the duplicate and retry.");
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

    public async Task<ImportJob?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM import_job ORDER BY created_at DESC, id DESC LIMIT 1",
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(command);
        return row?.ToDomain();
    }

    public async Task<ImportJobTotals?> GetTotalsAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            SELECT deudores_count::bigint AS deudores_count,
                   entidades_count::bigint AS entidades_count,
                   suma_total AS suma_total
            FROM import_job
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<TotalsRow>(command);
        return row is null ? null : new ImportJobTotals(row.DeudoresCount, row.EntidadesCount, row.SumaTotal);
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
                lines_skipped_control = @LinesSkippedControl,
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
                job.LinesSkippedControl,
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
                              lines_rejected, lines_skipped_control, lease_owner, heartbeat_at, created_at, started_at, finished_at
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
                    lines_skipped_control = @LinesSkippedControl,
                    heartbeat_at = @HeartbeatAt
                WHERE id = @Id AND lease_owner = @LeaseOwner AND status = 'Loading';
                """,
                new
                {
                    Id = importId,
                    checkpoint.BytesCommitted,
                    checkpoint.LinesProcessed,
                    checkpoint.LinesRejected,
                    checkpoint.LinesSkippedControl,
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
        await connection.ExecuteAsync($"SET work_mem = '{AggregationWorkMem}';");
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

            UPDATE import_job SET
                deudores_count = (SELECT COUNT(*) FROM deudor_agg WHERE import_id = @ImportId),
                entidades_count = (SELECT COUNT(*) FROM entidad_agg WHERE import_id = @ImportId),
                suma_total = (SELECT COALESCE(SUM(suma_total), 0) FROM deudor_agg WHERE import_id = @ImportId)
            WHERE id = @ImportId;
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
        await connection.ExecuteAsync($"SET maintenance_work_mem = '{IndexMaintenanceWorkMem}';");
        var command = new CommandDefinition(
            $"""
            CREATE INDEX IF NOT EXISTS ix_deudor_agg_{hex}_cuit ON deudor_agg (cuit) WHERE import_id = '{importId}';
            CREATE INDEX IF NOT EXISTS ix_deudor_agg_{hex}_ranking ON deudor_agg (suma_total DESC, cuit ASC) WHERE import_id = '{importId}';
            CREATE INDEX IF NOT EXISTS ix_entidad_agg_{hex}_entidad ON entidad_agg (codigo_entidad) WHERE import_id = '{importId}';
            """,
            commandTimeout: IndefiniteCommandTimeout,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        // VACUUM no puede ejecutarse dentro del batch implícito multi-statement de Npgsql:
        // una ejecución por sentencia. Marca las filas nuevas como all-visible (index-only
        // scans en los COUNT/SUM del query-service) y deja stats frescas pre-swap.
        foreach (var vacuumStatement in new[] { "VACUUM (ANALYZE) deudor_agg", "VACUUM (ANALYZE) entidad_agg" })
        {
            var vacuum = new CommandDefinition(
                vacuumStatement,
                commandTimeout: IndefiniteCommandTimeout,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(vacuum);
        }
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

        var command = new CommandDefinition(
            BuildImportStorageCleanupSql(importId),
            new { ImportId = importId },
            commandTimeout: IndefiniteCommandTimeout,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    /// <summary>Limpieza del storage de un import: partición de staging, índices parciales y agregados.</summary>
    private static string BuildImportStorageCleanupSql(Guid importId)
    {
        var hex = Hex(importId);
        return $"""
            DROP TABLE IF EXISTS {PartitionName("debt_staging", importId)};
            DROP INDEX IF EXISTS ix_deudor_agg_{hex}_cuit;
            DROP INDEX IF EXISTS ix_deudor_agg_{hex}_ranking;
            DROP INDEX IF EXISTS ix_entidad_agg_{hex}_entidad;
            DELETE FROM deudor_agg WHERE import_id = @ImportId;
            DELETE FROM entidad_agg WHERE import_id = @ImportId;
            """;
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
        var codigos = new List<int>();
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

    private static Task<int> UpsertEntidadesAsync(DbConnection connection, List<int> codigos, List<string> nombres, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            """
            INSERT INTO entidad (codigo, nombre)
            SELECT codigo, nombre
            FROM UNNEST(@Codigos::int[], @Nombres::text[]) AS t(codigo, nombre)
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

    private sealed class TotalsRow
    {
        public long DeudoresCount { get; set; }
        public long EntidadesCount { get; set; }
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
        public long LinesSkippedControl { get; set; }
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
                LeaseOwner, HeartbeatAt, CreatedAt, StartedAt, FinishedAt, SourceEtag, LinesSkippedControl));
    }
}
