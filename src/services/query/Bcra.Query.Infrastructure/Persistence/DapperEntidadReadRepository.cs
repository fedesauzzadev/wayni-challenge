using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;

namespace Bcra.Query.Infrastructure.Persistence;

public sealed class DapperEntidadReadRepository(IDbConnectionFactory connectionFactory) : IEntidadReadRepository
{
    public async Task<EntidadResumen?> GetEntidadAsync(Guid importId, int codigo, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            """
            SELECT
                e.codigo_entidad::int AS codigo_entidad,
                en.nombre,
                e.suma_total
            FROM entidad_agg e
            LEFT JOIN entidad en ON en.codigo = e.codigo_entidad
            WHERE e.import_id = @ImportId AND e.codigo_entidad = @Codigo
            """,
            new { ImportId = importId, Codigo = codigo },
            cancellationToken: cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<EntidadRow>(command);
        return row?.ToReadModel();
    }

    public async Task<EntidadesPage> GetEntidadesPageAsync(Guid importId, string? codigoFilter, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var filter = BuildCodigoFilter(codigoFilter);
        var command = new CommandDefinition(
            $"""
            SELECT COUNT(*)::bigint
            FROM entidad_agg e
            WHERE e.import_id = @ImportId{filter.Sql};

            SELECT
                e.codigo_entidad::int AS codigo_entidad,
                en.nombre,
                e.suma_total
            FROM entidad_agg e
            LEFT JOIN entidad en ON en.codigo = e.codigo_entidad
            WHERE e.import_id = @ImportId{filter.Sql}
            ORDER BY e.suma_total DESC, e.codigo_entidad ASC
            LIMIT @Limit;
            """,
            new { ImportId = importId, filter.Pattern, Limit = EntidadesPageSize },
            cancellationToken: cancellationToken);

        using var multi = await connection.QueryMultipleAsync(command);
        var total = await multi.ReadFirstAsync<long>();
        var rows = await multi.ReadAsync<EntidadRow>();
        return new EntidadesPage(total, rows.Select(row => row.ToReadModel()).ToList());
    }

    /// <summary>Tamaño de página del listado de entidades: las entidades son ~300 (una por banco),
    /// el filtro del lado del cliente opera sobre la página completa devuelta.</summary>
    private const int EntidadesPageSize = 1000;

    private static (string Sql, string? Pattern) BuildCodigoFilter(string? codigoFilter)
    {
        if (string.IsNullOrWhiteSpace(codigoFilter))
            return (string.Empty, null);

        return (" AND e.codigo_entidad::text LIKE @Pattern", codigoFilter.Trim() + "%");
    }

    private sealed class EntidadRow
    {
        public int CodigoEntidad { get; set; }
        public string? Nombre { get; set; }
        public decimal SumaTotal { get; set; }

        public EntidadResumen ToReadModel() => new(CodigoEntidad, Nombre, SumaTotal);
    }
}
