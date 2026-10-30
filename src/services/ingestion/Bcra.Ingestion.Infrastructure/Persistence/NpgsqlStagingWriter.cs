using Bcra.Ingestion.Domain.Parsing;
using Npgsql;
using NpgsqlTypes;

namespace Bcra.Ingestion.Infrastructure.Persistence;

/// <summary>
/// Ejecuta el COPY binario sobre la conexión compartida por el llamador: como la transacción
/// READ COMMITTED ya está abierta en ese conector, el lote a staging confirma atómicamente
/// con el checkpoint del job en la misma transacción.
/// </summary>
public static class NpgsqlStagingWriter
{
    public static async Task WriteBatchAsync(
        NpgsqlConnection connection,
        Guid importId,
        IReadOnlyList<ParsedDebtRecord> records,
        CancellationToken cancellationToken)
    {
        var importer = await connection.BeginBinaryImportAsync(
            $"COPY debt_staging_{importId:N} (import_id, entidad, cuit, situacion, monto) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        try
        {
            foreach (var record in records)
            {
                importer.StartRow();
                importer.Write(importId, NpgsqlDbType.Uuid);
                importer.Write(record.Entidad, NpgsqlDbType.Integer);
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
