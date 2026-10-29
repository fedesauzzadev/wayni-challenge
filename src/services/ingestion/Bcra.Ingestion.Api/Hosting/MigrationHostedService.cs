using Bcra.Ingestion.Infrastructure.Persistence.Migrations;

namespace Bcra.Ingestion.Api.Hosting;

public sealed class StartupState
{
    private volatile bool _migrationsApplied;

    public bool MigrationsApplied
    {
        get => _migrationsApplied;
        set => _migrationsApplied = value;
    }
}

public sealed partial class MigrationHostedService(
    DbUpMigrator migrator,
    StartupState startupState,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database migrations failed; stopping host")]
    private partial void LogMigrationsFailed(Exception exception);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            migrator.UpgradeWithRetry();
            startupState.MigrationsApplied = true;
            LogMigrationsApplied();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogMigrationsFailed(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
