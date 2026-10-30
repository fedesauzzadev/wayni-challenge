using Bcra.Query.Domain.Ports;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;

namespace Bcra.Query.Infrastructure.Persistence;

public sealed class ActiveImportResolver(IDbConnectionFactory connectionFactory) : IActiveImportResolver
{
    public async Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            "SELECT import_id FROM active_import WHERE id = 1",
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(command);
    }
}
