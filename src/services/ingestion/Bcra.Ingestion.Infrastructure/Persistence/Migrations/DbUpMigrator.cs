using Bcra.Shared.Infrastructure.Options;
using DbUp;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bcra.Ingestion.Infrastructure.Persistence.Migrations;

public sealed partial class DbUpMigrator
{
    private const string ReadonlyRoleName = "bcra_readonly";

    private readonly DatabaseOptions _options;
    private readonly ILogger<DbUpMigrator> _logger;

    public DbUpMigrator(IOptions<DatabaseOptions> options, ILogger<DbUpMigrator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

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
                LogRetryScheduled(attempt, _options.MigrationRetryCount, _options.MigrationRetryDelaySeconds);
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
            .WithExecutionTimeout(TimeSpan.FromSeconds(_options.MigrationCommandTimeoutSeconds))
            .LogTo(new DbUpLoggerAdapter(_logger))
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
        // PostgreSQL no acepta parámetros bind en ALTER ROLE (utility statement): se escapa "'" manualmente.
        // Idempotente: corre tras cada migración sin fallar aunque el rol ya exista con LOGIN activo
        // (el verificador SCRAM no es comparable, así que el password se re-establece en cada arranque).
        command.CommandText = $"""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{ReadonlyRoleName}') THEN
                    ALTER ROLE {ReadonlyRoleName} WITH LOGIN PASSWORD '{_options.ReadonlyPassword.Replace("'", "''")}';
                END IF;
            END
            $$;
            """;
        command.ExecuteNonQuery();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database not reachable; migration attempt {Attempt}/{TotalAttempts} will retry in {DelaySeconds}s")]
    private partial void LogRetryScheduled(int attempt, int totalAttempts, int delaySeconds);

    private sealed class DbUpLoggerAdapter(ILogger logger) : IUpgradeLog
    {
        // DbUp pasa plantillas string.Format dinámicas: no aplican plantillas constantes ni LoggerMessage.
#pragma warning disable CA1848, CA2254
        public void LogTrace(string format, params object[] args) => logger.LogTrace(format, args);

        public void LogDebug(string format, params object[] args) => logger.LogDebug(format, args);

        public void LogInformation(string format, params object[] args) => logger.LogInformation(format, args);

        public void LogWarning(string format, params object[] args) => logger.LogWarning(format, args);

        public void LogError(string format, params object[] args) => logger.LogError(format, args);

        public void LogError(Exception exception, string format, params object[] args) => logger.LogError(exception, format, args);
#pragma warning restore CA1848, CA2254
    }
}
