using Bcra.Notifier.Application.Ports;
using Bcra.Shared.Infrastructure.Persistence;
using Dapper;
using Npgsql;

namespace Bcra.Notifier.Infrastructure.Persistence;

/// <summary>
/// Lectura readonly de la tabla <c>notification_recipient</c> (creada por las migraciones de
/// ingestion). Tolerante a la tabla ausente al arranque (notifier puede levantar antes de la
/// primera migración): devuelve lista vacía y aplica el fallback a configuración.
/// </summary>
public sealed class DapperNotificationRecipientStore(IDbConnectionFactory connectionFactory) : INotificationRecipientStore
{
    private const string UndefinedTableSqlState = "42P01";

    public async Task<IReadOnlyList<string>> GetRecipientEmailsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = await connectionFactory.OpenAsync(cancellationToken);
            var command = new CommandDefinition(
                "SELECT email FROM notification_recipient ORDER BY id",
                cancellationToken: cancellationToken);
            var emails = await connection.QueryAsync<string>(command);
            return emails.ToList();
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTableSqlState)
        {
            return [];
        }
    }
}
