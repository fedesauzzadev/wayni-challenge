using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Infrastructure.Options;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;
using Microsoft.Extensions.Options;
using CuitValue = Bcra.Shared.Domain.ValueObjects.Cuit;

namespace Bcra.Query.Infrastructure.Persistence;

public sealed class DapperDeudorReadRepository(
    IDbConnectionFactory connectionFactory,
    IOptions<DatabaseOptions> databaseOptions) : IDeudorReadRepository
{
    private const string SelectColumns = """
        d.cuit,
        d.situacion_maxima::int AS situacion_maxima,
        d.suma_total,
        n.nombre
        """;

    public async Task<DeudorResumen?> GetDeudorAsync(Guid importId, CuitValue cuit, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM deudor_agg d
            LEFT JOIN deudor_nombre n ON n.cuit = d.cuit
            WHERE d.import_id = @ImportId AND d.cuit = @Cuit::char(11)
            """,
            new { ImportId = importId, Cuit = cuit.Value },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<DeudorRow>(command);
        return row?.ToReadModel();
    }

    public async Task<DeudoresPage> GetDeudoresPageAsync(Guid importId, int offset, int limit, bool omitirSinNombre, int? situacionMaxima, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var filters =
            (omitirSinNombre ? " AND n.nombre IS NOT NULL" : string.Empty)
            + (situacionMaxima is { } situacion ? " AND d.situacion_maxima = @Situacion" : string.Empty);
        var timeout = databaseOptions.Value.CommandTimeoutSeconds;

        // Sin filtros el total es el precomputado en import_job por la agregación:
        // un COUNT real sobre ~20M filas cuesta >10s con cache frío; acá es una lectura de 1 fila.
        // Con cualquier filtro activo el precomputado no aplica y se cuenta con los mismos filtros.
        var countCommand = new CommandDefinition(
            filters.Length == 0
                ? "SELECT deudores_count::bigint FROM import_job WHERE id = @ImportId"
                : $"""
                  SELECT COUNT(*)::bigint
                  FROM deudor_agg d
                  LEFT JOIN deudor_nombre n ON n.cuit = d.cuit
                  WHERE d.import_id = @ImportId{filters}
                  """,
            new { ImportId = importId, Situacion = situacionMaxima },
            commandTimeout: timeout,
            cancellationToken: cancellationToken);

        var pageCommand = new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM deudor_agg d
            LEFT JOIN deudor_nombre n ON n.cuit = d.cuit
            WHERE d.import_id = @ImportId{filters}
            ORDER BY d.suma_total DESC, d.cuit ASC
            LIMIT @Limit OFFSET @Offset;
            """,
            new { ImportId = importId, Situacion = situacionMaxima, Limit = limit, Offset = offset },
            commandTimeout: timeout,
            cancellationToken: cancellationToken);

        var total = await connection.QuerySingleAsync<long>(countCommand);
        var rows = await connection.QueryAsync<DeudorRow>(pageCommand);
        return new DeudoresPage(total, rows.Select(row => row.ToReadModel()).ToList());
    }

    public async Task<DeudorTotales> GetTotalesAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            SELECT
                deudores_count::bigint AS cantidad_deudores,
                entidades_count::bigint AS cantidad_entidades,
                suma_total AS suma_total
            FROM import_job
            WHERE id = @ImportId
            """,
            new { ImportId = importId },
            commandTimeout: databaseOptions.Value.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleAsync<TotalesRow>(command);
        return new DeudorTotales(row.CantidadDeudores, row.CantidadEntidades, row.SumaTotal);
    }

    private sealed class DeudorRow
    {
        public string Cuit { get; set; } = string.Empty;
        public int SituacionMaxima { get; set; }
        public decimal SumaTotal { get; set; }
        public string? Nombre { get; set; }

        public DeudorResumen ToReadModel() =>
            new(CuitValue.FromTrusted(Cuit), SituacionMaxima, SumaTotal, Nombre);
    }

    private sealed class TotalesRow
    {
        public long CantidadDeudores { get; set; }
        public long CantidadEntidades { get; set; }
        public decimal SumaTotal { get; set; }
    }
}
