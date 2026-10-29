using Bcra.Ingestion.Application.ImportExecution;
using Bcra.Ingestion.Domain.Parsing;
using Npgsql;
using NpgsqlTypes;

namespace Bcra.Ingestion.Infrastructure.Persistence;

public sealed class NpgsqlStagingWriter(IDbConnectionFactory connectionFactory) : IStagingWriter
{
    public async Task WriteBatchAsync(Guid importId, IReadOnlyList<ParsedDebtRecord> records, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var npgsqlConnection = (NpgsqlConnection)connection;

        var importer = await npgsqlConnection.BeginBinaryImportAsync(
            $"COPY debt_staging_{importId:N} (import_id, entidad, cuit, situacion, monto) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        try
        {
            foreach (var record in records)
            {
                importer.StartRow();
                importer.Write(importId, NpgsqlDbType.Uuid);
                importer.Write((short)record.Entidad, NpgsqlDbType.Smallint);
                importer.Write(record.Cuit, NpgsqlDbType.Char);
                importer.Write(record.Situacion, NpgsqlDbType.Smallint);
                importer.Write(record.Monto, NpgsqlDbType.Numeric);
            }

            await importer.CompleteAsync(cancellationToken);
        }
        finally
        {
            await importer.DisposeAsync();
        }
    }
}
