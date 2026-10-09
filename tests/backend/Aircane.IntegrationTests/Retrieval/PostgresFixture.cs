using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aircane.IntegrationTests.Retrieval;

/// <summary>
/// Starts one real PostgreSQL container with the pgvector extension, applies the EF Core
/// migrations once to a template database (the first migration runs
/// <c>CREATE EXTENSION IF NOT EXISTS vector</c>), and hands out cheap, isolated copies of that
/// template so each test starts from an empty, fully migrated schema.
/// </summary>
/// <remarks>
/// Requires a running Docker daemon. The image is <c>pgvector/pgvector:pg16</c>, the same one
/// <c>docker-compose.yml</c> uses for local development.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "aircane_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase(TemplateDatabase)
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Pooling is off for the template so no connection to it outlives the migration:
        // CREATE DATABASE ... TEMPLATE requires the template to have no active connections.
        var templateConnection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Pooling = false,
        }.ConnectionString;

        await using var db = CreateContext(templateConnection);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates a new database cloned from the migrated template and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"aircane_{Guid.NewGuid():N}";

        var admin = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres",
            Pooling = false,
        };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
        }.ConnectionString;
    }

    /// <summary>Creates a context using the same Npgsql + pgvector configuration as the API.</summary>
    public static AircaneDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;
        return new AircaneDbContext(options);
    }
}
