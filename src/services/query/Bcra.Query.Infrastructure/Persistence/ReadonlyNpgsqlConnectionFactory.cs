using System.Data.Common;
using Npgsql;

namespace Bcra.Query.Infrastructure.Persistence;

public interface IReadOnlyDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}

public sealed class ReadonlyNpgsqlConnectionFactory(string connectionString) : IReadOnlyDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
