using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Adventures;

public sealed class AdventureIndexingServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly FakeEmbeddingProvider _embeddingProvider;
    private readonly AdventureIndexingService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AdventureIndexingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _embeddingProvider = new FakeEmbeddingProvider();
        _service = new AdventureIndexingService(
            _db,
            _embeddingProvider,
            NullLogger<AdventureIndexingService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Helper Methods ────────────────────────────────────────────────────────

    private GeneratedAdventure CreateFullAdventure(Guid? id = null)
    {
        var adventureId = id ?? Guid.NewGuid();

        var request = new GenerateAdventureRequest
        {
            Mode = "Group",
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            PartySize = 4,
            AverageLevel = 5,
            Tone = "epic",
            Length = "one-shot",
            Difficulty = "medium",
            CombatRatio = 40,
            ExplorationRatio = 30,
            RoleplayRatio = 30,
        };

        var scenes = new List<GeneratedScene>
        {
            new()
            {
                SceneId = "scene-1",
                Title = "The Village Square",
                Description = "A bustling village square with a fountain.",
                SceneType = "roleplay",
                ConnectsTo = ["scene-2"],
                ReadAloudText = "You arrive at a peaceful village square.",
                DmNotes = "The innkeeper knows about the dungeon entrance.",
            },
            new()
            {
                SceneId = "scene-2",
                Title = "The Dark Dungeon",
                Description = "A damp dungeon with flickering torches.",
                SceneType = "combat",
                ConnectsTo = [],
                ReadAloudText = null,
                DmNotes = "Trap on the second corridor.",
            },
        };

        var npcs = new List<GeneratedNpc>
        {
            new()
            {
                Name = "Elder Moira",
                Role = "quest giver",
                Personality = "Wise and cautious",
                StatsSummary = "Commoner",
                SceneIds = ["scene-1"],
                Faction = "Village Council",
            },
        };

        var encounters = new List<GeneratedEncounter>
        {
            new()
            {
                Title = "Skeleton Ambush",
                SceneId = "scene-2",
                Enemies = [new EncounterCreature { Name = "Skeleton", Count = 4, ChallengeRating = "1/4" }],
                Difficulty = "medium",
                Tactics = "Attack in groups from the shadows",
                Environment = "Narrow corridor",
            },
        };

        var clues = new AdventureClues
        {
            Secrets =
            [
                new AdventureSecret
                {
                    Title = "Hidden Passage",
                    Content = "A secret tunnel behind the altar leads to the treasure room.",
                    SceneId = "scene-2",
                    DiscoveryMethod = "DC 15 Investigation",
                },
            ],
            Handouts =
            [
                new AdventureHandout
                {
                    Title = "Ancient Map",
                    Content = "A faded map showing the dungeon layout.",
                    SceneId = "scene-1",
                },
            ],
            FailForwardPaths =
            [
                new FailForwardPath
                {
                    Trigger = "Players cannot find the dungeon entrance",
                    Resolution = "A villager approaches and offers to guide them",
                    SceneId = "scene-1",
                },
            ],
        };

        return new GeneratedAdventure
        {
            Id = adventureId,
            Title = "The Lost Temple",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            ScenesJson = JsonSerializer.Serialize(scenes, JsonOptions),
            NpcsJson = JsonSerializer.Serialize(npcs, JsonOptions),
            EncountersJson = JsonSerializer.Serialize(encounters, JsonOptions),
            CluesJson = JsonSerializer.Serialize(clues, JsonOptions),
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private async Task<GeneratedAdventure> SeedAdventureAsync(GeneratedAdventure? adventure = null)
    {
        var entity = adventure ?? CreateFullAdventure();
        _db.GeneratedAdventures.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    // ── IndexAdventureAsync Tests ─────────────────────────────────────────────

    [Fact]
    public async Task IndexAdventureAsync_throws_when_adventure_not_found()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.IndexAdventureAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_source_document_with_generated_type()
    {
        var adventure = await SeedAdventureAsync();

        var sourceDocId = await _service.IndexAdventureAsync(adventure.Id);

        var sourceDoc = await _db.SourceDocuments.FindAsync(sourceDocId);
        Assert.NotNull(sourceDoc);
        Assert.Equal(SourceType.Generated, sourceDoc.SourceType);
        Assert.Equal(adventure.Title, sourceDoc.Title);
        Assert.Equal(adventure.GameSystem, sourceDoc.GameSystem);
        Assert.Equal(adventure.Ruleset, sourceDoc.Ruleset);
        Assert.Equal(ImportStatus.Completed, sourceDoc.ImportStatus);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_chunks_for_scenes()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var sceneChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "scene")
            .ToListAsync();

        Assert.Equal(2, sceneChunks.Count);
        Assert.All(sceneChunks, c => Assert.Equal(ContentVisibility.Public, c.Visibility));
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_chunks_for_read_aloud_text()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var readAloudChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "read-aloud")
            .ToListAsync();

        // Only scene-1 has read-aloud text
        Assert.Single(readAloudChunks);
        Assert.Equal(ContentVisibility.Public, readAloudChunks[0].Visibility);
        Assert.Contains("You arrive at a peaceful village square", readAloudChunks[0].Text);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_dm_notes_chunks_with_dm_only_visibility()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var dmNotesChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "dm-notes")
            .ToListAsync();

        Assert.Equal(2, dmNotesChunks.Count);
        Assert.All(dmNotesChunks, c => Assert.Equal(ContentVisibility.DMOnly, c.Visibility));
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_npc_chunks_with_dm_only_visibility()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var npcChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "npc")
            .ToListAsync();

        Assert.Single(npcChunks);
        Assert.Equal(ContentVisibility.DMOnly, npcChunks[0].Visibility);
        Assert.Contains("Elder Moira", npcChunks[0].Text);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_encounter_chunks_with_dm_only_visibility()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var encounterChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "encounter")
            .ToListAsync();

        Assert.Single(encounterChunks);
        Assert.Equal(ContentVisibility.DMOnly, encounterChunks[0].Visibility);
        Assert.Contains("Skeleton Ambush", encounterChunks[0].Text);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_secret_chunks_with_hidden_visibility()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var secretChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "secret")
            .ToListAsync();

        Assert.Single(secretChunks);
        Assert.Equal(ContentVisibility.Hidden, secretChunks[0].Visibility);
        Assert.Contains("Hidden Passage", secretChunks[0].Text);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_handout_chunks_with_dm_only_visibility()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var handoutChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "handout")
            .ToListAsync();

        Assert.Single(handoutChunks);
        Assert.Equal(ContentVisibility.DMOnly, handoutChunks[0].Visibility);
        Assert.Contains("Ancient Map", handoutChunks[0].Text);
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_fail_forward_chunks()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var failForwardChunks = await _db.DocumentChunks
            .Where(c => c.ChunkType == "fail-forward")
            .ToListAsync();

        Assert.Single(failForwardChunks);
        Assert.Equal(ContentVisibility.DMOnly, failForwardChunks[0].Visibility);
    }

    [Fact]
    public async Task IndexAdventureAsync_generates_embeddings_for_all_chunks()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        // The fake embedding provider was called for all chunks
        var totalChunks = await _db.DocumentChunks.CountAsync();
        Assert.True(totalChunks > 0);
        Assert.Equal(totalChunks, _embeddingProvider.CallCount);
    }

    [Fact]
    public async Task IndexAdventureAsync_handles_adventure_with_no_content()
    {
        var adventure = new GeneratedAdventure
        {
            Id = Guid.NewGuid(),
            Title = "Empty Adventure",
            Status = GeneratedAdventureStatus.Approved,
            RequestJson = "{}",
            ScenesJson = null,
            NpcsJson = null,
            EncountersJson = null,
            CluesJson = null,
            Ruleset = "D&D 5e 2014",
            GameSystem = "D&D 5e 2014",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await SeedAdventureAsync(adventure);

        var sourceDocId = await _service.IndexAdventureAsync(adventure.Id);

        var sourceDoc = await _db.SourceDocuments.FindAsync(sourceDocId);
        Assert.NotNull(sourceDoc);
        Assert.Equal(ImportStatus.Completed, sourceDoc.ImportStatus);

        var chunks = await _db.DocumentChunks.CountAsync();
        Assert.Equal(0, chunks);
    }

    [Fact]
    public async Task IndexAdventureAsync_assigns_sequential_chunk_indices()
    {
        var adventure = await SeedAdventureAsync();

        await _service.IndexAdventureAsync(adventure.Id);

        var chunks = await _db.DocumentChunks
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public async Task IndexAdventureAsync_creates_correct_total_chunk_count()
    {
        var adventure = await SeedAdventureAsync();

        var sourceDocId = await _service.IndexAdventureAsync(adventure.Id);

        // 2 scenes + 1 read-aloud + 2 dm-notes + 1 npc + 1 encounter + 1 secret + 1 handout + 1 fail-forward = 10
        var totalChunks = await _db.DocumentChunks.CountAsync();
        Assert.Equal(10, totalChunks);
    }

    [Fact]
    public async Task IndexAdventureAsync_sets_source_document_id_on_all_chunks()
    {
        var adventure = await SeedAdventureAsync();

        var sourceDocId = await _service.IndexAdventureAsync(adventure.Id);

        var chunks = await _db.DocumentChunks.ToListAsync();
        Assert.All(chunks, c => Assert.Equal(sourceDocId, c.SourceDocumentId));
    }

    // ── Fake Embedding Provider ───────────────────────────────────────────────

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions => 768;
        public string ProviderName => "Fake";
        public int CallCount { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new float[768]);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            CallCount = texts.Count;
            var results = texts.Select(_ => new float[768]).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(results);
        }
    }
}
