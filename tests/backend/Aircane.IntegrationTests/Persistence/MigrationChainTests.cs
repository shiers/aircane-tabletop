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
    private const string TurnAdvanceAndChatMigration = "AddTurnAdvanceModeAndChatMessages";
    private const string RejoinMigration = "AddParticipantRejoinAndReplace";

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
        // Only the columns this migration added are read: the database is at this migration, not at the latest,
        // so selecting the whole entity would ask for columns that later migrations add.
        var run = await db.AdventureSessions.AsNoTracking()
            .Where(s => s.Id == runId)
            .Select(s => new { s.IsGroup, s.TurnOrder, s.ActiveParticipantId, s.CurrentRound })
            .SingleAsync();
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

    [Fact]
    public async Task TurnAdvanceAndChatMigration_DefaultsExistingRunsToManualOnly_AndCreatesTheChatTable()
    {
        await using var db = PostgresFixture.CreateContext(await CreateEmptyDatabaseAsync());
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(TurnAdvanceAndChatMigration, StringComparison.Ordinal));
        Assert.True(index > 0, $"Migration {TurnAdvanceAndChatMigration} was not found in the chain.");

        await migrator.MigrateAsync(all[index - 1]);

        // A group run that exists before the setting does. Foreign keys are skipped (superuser connection).
        var runId = Guid.NewGuid();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdventureSessions"
                ("Id", "GeneratedAdventureId", "CampaignId", "CurrentSceneId", "Status", "StartedAt", "LastActivityAt",
                 "CompletedSceneIds", "NpcStates", "RevealedClueIds", "RevealedSecretIds", "EncounterOutcomes",
                 "IsGroup", "TurnOrder", "CurrentRound")
            VALUES
                ({runId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'scene-1', 1, now(), now(),
                 '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb,
                 true, '[]'::jsonb, 1)
            """);

        await migrator.MigrateAsync(all[index]);

        // Existing runs keep today's behaviour: the turn moves only when a player or the host says so.
        var mode = await db.AdventureSessions.AsNoTracking().Where(s => s.Id == runId).Select(s => s.TurnAdvance).SingleAsync();
        Assert.Equal(Aircane.Domain.Enums.TurnAdvanceMode.ManualOnly, mode);

        // And the chat table takes a message and pages it back.
        var sessionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ChatMessages" ("Id", "SessionId", "SenderId", "SenderName", "SenderRole", "Text", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {sessionId}, {Guid.NewGuid()}, 'Alice', 2, 'hello', now())
            """);
        Assert.Equal(1, await db.ChatMessages.AsNoTracking().CountAsync(m => m.SessionId == sessionId));

        await migrator.MigrateAsync(all[index - 1]);   // runs Down()
        await migrator.MigrateAsync(all[index]);       // and applies cleanly again
    }

    [Fact]
    public async Task RejoinMigration_LeavesExistingParticipantsWithNoSecretAndTheirSeat_AndRollsBack()
    {
        await using var db = PostgresFixture.CreateContext(await CreateEmptyDatabaseAsync());
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(RejoinMigration, StringComparison.Ordinal));
        Assert.True(index > 0, $"Migration {RejoinMigration} was not found in the chain.");

        await migrator.MigrateAsync(all[index - 1]);

        // A participant who joined before rejoin secrets existed. Foreign keys are skipped (superuser connection).
        var participantId = Guid.NewGuid();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SessionParticipants"
                ("Id", "SessionId", "DisplayName", "Role", "JoinedAt", "LastSeenAt", "IsApproved", "IsDenied", "CreatedAt")
            VALUES
                ({participantId}, {Guid.NewGuid()}, 'Bifur', 2, now(), now(), true, false, now())
            """);

        await migrator.MigrateAsync(all[index]);

        // They keep their seat; they simply cannot rejoin with a secret (they never sent one).
        var row = await db.SessionParticipants.AsNoTracking().SingleAsync(p => p.Id == participantId);
        Assert.True(row.IsApproved);
        Assert.Null(row.RejoinSecretHash);
        Assert.Null(row.ReplacedByParticipantId);

        await migrator.MigrateAsync(all[index - 1]);   // runs Down()
        await migrator.MigrateAsync(all[index]);       // and applies cleanly again
    }

    private static Task<List<string>> GroupPlayColumnsAsync(AircaneDbContext db) =>
        db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_name = 'AdventureSessions'
              AND column_name IN ('IsGroup', 'TurnOrder', 'ActiveParticipantId', 'CurrentRound')
            ORDER BY column_name
            """).ToListAsync();
}
