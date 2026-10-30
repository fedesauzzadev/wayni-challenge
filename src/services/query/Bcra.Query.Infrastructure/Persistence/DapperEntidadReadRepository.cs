using Bcra.Query.Domain.Ports;
using Bcra.Query.Domain.ReadModels;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;

namespace Bcra.Query.Infrastructure.Persistence;

public sealed class DapperEntidadReadRepository(IDbConnectionFactory connectionFactory) : IEntidadReadRepository
{
    static DapperEntidadReadRepository() => DefaultTypeMap.MatchNamesWithUnderscores = true;

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

    private sealed class EntidadRow
    {
        public int CodigoEntidad { get; set; }
        public string? Nombre { get; set; }
        public decimal SumaTotal { get; set; }

        public EntidadResumen ToReadModel() => new(CodigoEntidad, Nombre, SumaTotal);
    }
}
