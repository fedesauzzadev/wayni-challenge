using System.Data;
using Bcra.Ingestion.Domain.ImportJobs;
using Dapper;
using Npgsql;

namespace Bcra.Ingestion.Infrastructure.Persistence;

public sealed class DapperImportJobRepository(IDbConnectionFactory connectionFactory) : IImportJobRepository
{
    private const string UniqueViolationSqlState = "23505";
    private const string SelectColumns = """
        id, source, etag, size, periodo, status, bytes_committed, lines_processed, lines_rejected,
        lease_owner, heartbeat_at, created_at, started_at, finished_at
        """;

    static DapperImportJobRepository() => DefaultTypeMap.MatchNamesWithUnderscores = true;

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
                    RETURNING id, source, etag, size, periodo, status, bytes_committed, lines_processed,
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
        var staging = PartitionName("debt_staging", importId);
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"""
            CREATE INDEX IF NOT EXISTS ix_debt_staging_{hex}_entidad ON {staging} (entidad);
            CREATE INDEX IF NOT EXISTS ix_deudor_agg_{hex}_cuit ON deudor_agg (cuit) WHERE import_id = '{importId}';
            CREATE INDEX IF NOT EXISTS ix_entidad_agg_{hex}_entidad ON entidad_agg (codigo_entidad) WHERE import_id = '{importId}';
            """,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<Guid?> SwapActiveImportAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var lockCommand = new CommandDefinition(
            "SELECT import_id FROM active_import WHERE id = 1 FOR UPDATE;",
            transaction: transaction,
            cancellationToken: cancellationToken);
        var previous = await connection.QuerySingleOrDefaultAsync<Guid?>(lockCommand);

        var swapCommand = new CommandDefinition(
            "UPDATE active_import SET import_id = @ImportId WHERE id = 1;",
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
            "SELECT import_id FROM active_import WHERE id = 1;",
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
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static string Hex(Guid importId) => importId.ToString("N");

    private static string PartitionName(string prefix, Guid importId) => $"{prefix}_{Hex(importId)}";

    private sealed class ImportJobRow
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string? Etag { get; set; }
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
            Id, Source, Etag, Size, Periodo,
            Enum.Parse<ImportJobStatus>(Status),
            BytesCommitted, LinesProcessed, LinesRejected,
            LeaseOwner, HeartbeatAt, CreatedAt, StartedAt, FinishedAt);
    }
}
