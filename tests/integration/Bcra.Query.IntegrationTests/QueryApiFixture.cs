using Bcra.Ingestion.Infrastructure.Options;
using Bcra.Ingestion.Infrastructure.Persistence.Migrations;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Bcra.Query.IntegrationTests;

public sealed class QueryApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private QueryApiFactory? _factory;

    public string ConnectionString => _container.GetConnectionString();

    public HttpClient CreateClient() => Factory.CreateClient();

    private QueryApiFactory Factory => _factory ??= new QueryApiFactory(ConnectionString);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        new DbUpMigrator(Options.Create(new DatabaseOptions { ConnectionString = ConnectionString }))
            .UpgradeWithRetry();
    }

    /// <summary>Aísla cada test: sin import activo y sin agregados residuales.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM deudor_agg;
            DELETE FROM entidad_agg;
            DELETE FROM entidad;
            DELETE FROM deudor_nombre;
            DELETE FROM import_job;
            UPDATE active_import SET import_id = NULL WHERE id = 1;
            """);
    }

    /// <summary>Publica un import activo con cuatro deudores y dos entidades conocidas.</summary>
    public async Task SeedActiveImportAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO import_job (id, source, status, created_at)
            VALUES (@Id, 'integration-test', 'Completed', NOW());

            UPDATE active_import SET import_id = @Id WHERE id = 1;

            INSERT INTO deudor_agg (import_id, cuit, situacion_maxima, suma_total) VALUES
            (@Id, '20123456784', 5, 1000.5),
            (@Id, '20987654321', 3, 500.0),
            (@Id, '20000000009', 1, 250.2),
            (@Id, '27000000008', 1, 100.0);

            INSERT INTO entidad_agg (import_id, codigo_entidad, suma_total) VALUES
            (@Id, 266, 900.5),
            (@Id, 7, 850.0);

            INSERT INTO entidad (codigo, nombre) VALUES
            (266, 'BANCO DE LA NACION ARGENTINA'),
            (7, 'BANCO PROVINCIA')
            ON CONFLICT (codigo) DO NOTHING;

            INSERT INTO deudor_nombre (cuit, nombre) VALUES
            ('20123456784', 'PEREZ JUAN CARLOS')
            ON CONFLICT (cuit) DO NOTHING;
            """,
            new { Id = Guid.NewGuid() });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

public sealed class QueryApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString,
            ["Query:TopMax"] = "2"
        }));
    }
}

[CollectionDefinition("query-postgres")]
public sealed class QueryApiTestCluster : ICollectionFixture<QueryApiFixture>;
