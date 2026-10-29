using Bcra.Ingestion.Domain.ImportJobs;
using Bcra.Ingestion.Infrastructure.Options;
using Bcra.Ingestion.Infrastructure.Persistence;
using Bcra.Ingestion.Infrastructure.Persistence.Migrations;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Bcra.Ingestion.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private IDbConnectionFactory? _connectionFactory;

    public string ConnectionString => _container.GetConnectionString();

    public IDbConnectionFactory ConnectionFactory =>
        _connectionFactory ??= new NpgsqlConnectionFactory(ConnectionString);

    public DapperImportJobRepository CreateRepository() => new(ConnectionFactory);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        new DbUpMigrator(Options.Create(new DatabaseOptions { ConnectionString = ConnectionString }))
            .UpgradeWithRetry();
    }

    /// <summary>Aísla cada test: sin imports, sin lease y sin particiones residuales (orden de ejecución impredecible).</summary>
    public async Task ResetAsync()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DO $$
            DECLARE part record;
            BEGIN
                FOR part IN
                    SELECT c.relname
                    FROM pg_inherits i
                    JOIN pg_class c ON c.oid = i.inhrelid
                    JOIN pg_class p ON p.oid = i.inhparent
                    WHERE p.relname = 'debt_staging'
                LOOP
                    EXECUTE 'DROP TABLE IF EXISTS ' || part.relname;
                END LOOP;
            END
            $$;

            DELETE FROM deudor_agg;
            DELETE FROM entidad_agg;
            DELETE FROM entidad;
            DELETE FROM deudor_nombre;
            DELETE FROM import_job;
            """);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresTestCluster : ICollectionFixture<PostgresFixture>;
