using Bcra.Shared.Infrastructure.Options;
using DbUp;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bcra.Ingestion.Infrastructure.Persistence.Migrations;

public sealed class DbUpMigrator
{
    private readonly DatabaseOptions _options;

    public DbUpMigrator(IOptions<DatabaseOptions> options) => _options = options.Value;

    public void UpgradeWithRetry()
    {
        for (var attempt = 1; attempt <= _options.MigrationRetryCount; attempt++)
        {
            try
            {
                UpgradeOnce();
                return;
            }
            catch (NpgsqlException) when (attempt < _options.MigrationRetryCount)
            {
                Thread.Sleep(TimeSpan.FromSeconds(_options.MigrationRetryDelaySeconds));
            }
        }
    }

    private void UpgradeOnce()
    {
        var upgrade = DeployChanges.To
            .PostgresqlDatabase(_options.ConnectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrator).Assembly)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrade.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        EnableReadonlyLogin();
    }

    private void EnableReadonlyLogin()
    {
        if (string.IsNullOrEmpty(_options.ReadonlyPassword))
        {
            return;
        }

        using var connection = new NpgsqlConnection(_options.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER ROLE bcra_readonly WITH LOGIN PASSWORD '{_options.ReadonlyPassword.Replace("'", "''")}'";
        command.ExecuteNonQuery();
    }
}
