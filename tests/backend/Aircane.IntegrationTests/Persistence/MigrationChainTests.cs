using Aircane.IntegrationTests.Retrieval;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aircane.IntegrationTests.Persistence;

/// <summary>
/// Applies the EF Core migration chain to an empty PostgreSQL, so a migration that only breaks on a
/// real database (the in-memory provider never runs them) fails here rather than at first start.
/// Each test gets its own empty database in one throwaway pgvector container.
/// </summary>
/// <remarks>Requires a running Docker daemon.</remarks>
public sealed class MigrationChainTests : IAsyncLifetime
{
    private const string GroupPlayMigration = "AddAdventureSessionGroupPlay";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("postgres")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A new, empty database in the container.</summary>
    private async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"aircane_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            Pooling = false,
        }.ConnectionString;
    }

    /// <summary>The ID of the migration that comes right before <see cref="GroupPlayMigration"/>.</summary>
    private static (string Previous, string GroupPlay) GroupPlayMigrationIds(AircaneDbContext db)
    {
        var all = db.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(GroupPlayMigration, StringComparison.Ordinal));
        Assert.True(index > 0, $"Migration {GroupPlayMigration} was not found in the chain.");
        return (all[index - 1], all[index]);
    }

    [Fact]
    public async Task Migrations_ApplyToEmptyDatabase_AndLeaveNothingPending()
    {
        await using var db = PostgresFixture.CreateContext(await CreateEmptyDatabaseAsync());

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            db.Database.GetMigrations().ToList(),
            (await db.Database.GetAppliedMigrationsAsync()).ToList());
        // The model and the migrated schema agree: the table the last feature added is queryable.
        Assert.Equal(0, await db.AdventureSessions.CountAsync());
    }

    [Fact]
    public async Task GroupPlayMigration_BackfillsExistingRuns_AsSoloRuns()
    {
        await using var db = PostgresFixture.CreateContext(await CreateEmptyDatabaseAsync());
        var migrator = db.GetService<IMigrator>();
        var (previous, groupPlay) = GroupPlayMigrationIds(db);

        await migrator.MigrateAsync(previous);

        // A run created before group play existed. Foreign keys are skipped (this connection is
        // a superuser) because only the AdventureSessions row matters here.
        var runId = Guid.NewGuid();
        await db.Database.OpenConnectionAsync();   // keep one connection: the setting below is per-connection
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdventureSessions"
                ("Id", "GeneratedAdventureId", "CampaignId", "CurrentSceneId", "Status", "StartedAt", "LastActivityAt",
                 "CompletedSceneIds", "NpcStates", "RevealedClueIds", "RevealedSecretIds", "EncounterOutcomes")
            VALUES
                ({runId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'scene-1', 1, now(), now(),
                 '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb)
            """);

        await migrator.MigrateAsync(groupPlay);

        // The new jsonb column must default to an empty list: an empty string is not valid JSON and
        // would make the existing run unreadable.
        var run = await db.AdventureSessions.AsNoTracking().SingleAsync(s => s.Id == runId);
        Assert.False(run.IsGroup);
        Assert.Empty(run.TurnOrder);
        Assert.Null(run.ActiveParticipantId);
        Assert.Equal(0, run.CurrentRound);
    }

    [Fact]
    public async Task GroupPlayMigration_CanBeRolledBack_AndReapplied()
    {
        await using var db = PostgresFixture.CreateContext(await CreateEmptyDatabaseAsync());
        var migrator = db.GetService<IMigrator>();
        var (previous, groupPlay) = GroupPlayMigrationIds(db);

        await migrator.MigrateAsync(groupPlay);
        await migrator.MigrateAsync(previous);   // runs Down()

        var columnsAfterDown = await GroupPlayColumnsAsync(db);
        Assert.Empty(columnsAfterDown);

        await migrator.MigrateAsync(groupPlay);
        Assert.Equal(
            ["ActiveParticipantId", "CurrentRound", "IsGroup", "TurnOrder"],
            await GroupPlayColumnsAsync(db));
    }

    private static Task<List<string>> GroupPlayColumnsAsync(AircaneDbContext db) =>
        db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_name = 'AdventureSessions'
              AND column_name IN ('IsGroup', 'TurnOrder', 'ActiveParticipantId', 'CurrentRound')
            ORDER BY column_name
            """).ToListAsync();
}
