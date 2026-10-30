using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;
using CuitValue = Bcra.Shared.Domain.ValueObjects.Cuit;

namespace Bcra.Query.Infrastructure.Persistence;

public sealed class DapperDeudorReadRepository(IDbConnectionFactory connectionFactory) : IDeudorReadRepository
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
            WHERE d.import_id = @ImportId AND d.cuit = @Cuit
            """,
            new { ImportId = importId, Cuit = cuit.Value },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<DeudorRow>(command);
        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<DeudorResumen>> GetTopDeudoresAsync(Guid importId, int count, bool omitirSinNombre, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var nombreFilter = omitirSinNombre ? " AND n.nombre IS NOT NULL" : string.Empty;
        var command = new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM deudor_agg d
            LEFT JOIN deudor_nombre n ON n.cuit = d.cuit
            WHERE d.import_id = @ImportId{nombreFilter}
            ORDER BY d.suma_total DESC, d.cuit ASC
            LIMIT @Count
            """,
            new { ImportId = importId, Count = count },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<DeudorRow>(command);
        return rows.Select(row => row.ToReadModel()).ToList();
    }

    public async Task<DeudorTotales> GetTotalesAsync(Guid importId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            SELECT
                (SELECT COUNT(*) FROM deudor_agg WHERE import_id = @ImportId)::bigint AS cantidad_deudores,
                (SELECT COUNT(*) FROM entidad_agg WHERE import_id = @ImportId)::bigint AS cantidad_entidades,
                COALESCE((SELECT SUM(suma_total) FROM deudor_agg WHERE import_id = @ImportId), 0) AS suma_total
            """,
            new { ImportId = importId },
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
