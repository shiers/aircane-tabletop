using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Converts generated adventure content into searchable DocumentChunks with embeddings,
/// enabling the RAG pipeline to retrieve adventure context during play sessions.
/// </summary>
public sealed class AdventureIndexingService : IAdventureIndexingService
{
    private readonly AircaneDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<AdventureIndexingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AdventureIndexingService(
        AircaneDbContext db,
        IEmbeddingProvider embeddingProvider,
        ILogger<AdventureIndexingService> logger)
    {
        _db = db;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> IndexAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var adventure = await _db.GeneratedAdventures
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adventureId, cancellationToken);

        if (adventure is null)
            throw new ArgumentException($"Adventure {adventureId} not found.", nameof(adventureId));

        _logger.LogInformation("Indexing generated adventure {AdventureId} ({Title})", adventureId, adventure.Title);

        // Create a SourceDocument record for this generated adventure
        var sourceDocument = new SourceDocument(
            title: adventure.Title,
            originalFileName: $"generated-adventure-{adventureId}.json",
            sourceType: SourceType.Generated,
            sourceMode: SourceMode.Upload, // Generated content uses Upload mode (app-managed)
            gameSystem: adventure.GameSystem,
            ruleset: adventure.Ruleset,
            sourcePath: $"generated://{adventureId}",
            visibility: ContentVisibility.DMOnly,
            importStatus: ImportStatus.Processing,
            tags: ["generated", "adventure"]);

        _db.SourceDocuments.Add(sourceDocument);
        await _db.SaveChangesAsync(cancellationToken);

        // Build chunks from adventure content
        var chunks = BuildChunks(adventure, sourceDocument.Id);

        if (chunks.Count == 0)
        {
            _logger.LogWarning("No indexable content found in adventure {AdventureId}", adventureId);
            sourceDocument.ImportStatus = ImportStatus.Completed;
            _db.SourceDocuments.Update(sourceDocument);
            await _db.SaveChangesAsync(cancellationToken);
            return sourceDocument.Id;
        }

        // Generate embeddings for all chunks
        var texts = chunks.Select(c => c.Text).ToList();
        var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(texts, cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = new Vector(embeddings[i]);
        }

        _db.DocumentChunks.AddRange(chunks);

        // Mark import as completed
        sourceDocument.ImportStatus = ImportStatus.Completed;
        _db.SourceDocuments.Update(sourceDocument);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Indexed adventure {AdventureId}: created {ChunkCount} chunks with embeddings",
            adventureId, chunks.Count);

        return sourceDocument.Id;
    }

    /// <summary>
    /// Builds DocumentChunk records from all adventure content sections.
    /// Visibility rules:
    /// - Scene descriptions and read-aloud text: Public
    /// - DM notes: DMOnly
    /// - Secrets: Hidden
    /// </summary>
    internal List<DocumentChunk> BuildChunks(GeneratedAdventure adventure, Guid sourceDocumentId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        // Index scenes
        var scenes = DeserializeOrNull<List<GeneratedScene>>(adventure.ScenesJson);
        if (scenes is not null)
        {
            foreach (var scene in scenes)
            {
                // Scene description — Public (players can see scene descriptions during play)
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"Scene: {scene.Title}\n{scene.Description}",
                    sectionTitle: scene.Title,
                    chunkType: "scene",
                    visibility: ContentVisibility.Public,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneId = scene.SceneId,
                        sceneType = scene.SceneType,
                        connectsTo = scene.ConnectsTo,
                    }, JsonOptions)));

                // Read-aloud text — Public
                if (!string.IsNullOrWhiteSpace(scene.ReadAloudText))
                {
                    chunks.Add(new DocumentChunk(
                        sourceDocumentId: sourceDocumentId,
                        chunkIndex: chunkIndex++,
                        text: $"Read Aloud ({scene.Title}): {scene.ReadAloudText}",
                        sectionTitle: $"{scene.Title} - Read Aloud",
                        chunkType: "read-aloud",
                        visibility: ContentVisibility.Public,
                        metadataJson: JsonSerializer.Serialize(new
                        {
                            adventureId = adventure.Id,
                            sceneId = scene.SceneId,
                        }, JsonOptions)));
                }

                // DM notes — DMOnly
                if (!string.IsNullOrWhiteSpace(scene.DmNotes))
                {
                    chunks.Add(new DocumentChunk(
                        sourceDocumentId: sourceDocumentId,
                        chunkIndex: chunkIndex++,
                        text: $"DM Notes ({scene.Title}): {scene.DmNotes}",
                        sectionTitle: $"{scene.Title} - DM Notes",
                        chunkType: "dm-notes",
                        visibility: ContentVisibility.DMOnly,
                        metadataJson: JsonSerializer.Serialize(new
                        {
                            adventureId = adventure.Id,
                            sceneId = scene.SceneId,
                        }, JsonOptions)));
                }
            }
        }

        // Index NPCs
        var npcs = DeserializeOrNull<List<GeneratedNpc>>(adventure.NpcsJson);
        if (npcs is not null)
        {
            foreach (var npc in npcs)
            {
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"NPC: {npc.Name} ({npc.Role})\nPersonality: {npc.Personality}\nStats: {npc.StatsSummary}",
                    sectionTitle: npc.Name,
                    chunkType: "npc",
                    visibility: ContentVisibility.DMOnly,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneIds = npc.SceneIds,
                        role = npc.Role,
                        faction = npc.Faction,
                    }, JsonOptions)));
            }
        }

        // Index encounters
        var encounters = DeserializeOrNull<List<GeneratedEncounter>>(adventure.EncountersJson);
        if (encounters is not null)
        {
            foreach (var encounter in encounters)
            {
                var enemyList = string.Join(", ", encounter.Enemies.Select(e => $"{e.Count}x {e.Name} (CR {e.ChallengeRating})"));
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"Encounter: {encounter.Title}\nDifficulty: {encounter.Difficulty}\nEnemies: {enemyList}\nTactics: {encounter.Tactics}",
                    sectionTitle: encounter.Title,
                    chunkType: "encounter",
                    visibility: ContentVisibility.DMOnly,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneId = encounter.SceneId,
                        difficulty = encounter.Difficulty,
                    }, JsonOptions)));
            }
        }

        // Index clues and secrets
        var clues = DeserializeOrNull<AdventureClues>(adventure.CluesJson);
        if (clues is not null)
        {
            // Secrets — Hidden (until revealed by DM)
            foreach (var secret in clues.Secrets)
            {
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"Secret: {secret.Title}\n{secret.Content}\nDiscovery: {secret.DiscoveryMethod}",
                    sectionTitle: secret.Title,
                    chunkType: "secret",
                    visibility: ContentVisibility.Hidden,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneId = secret.SceneId,
                        discoveryMethod = secret.DiscoveryMethod,
                    }, JsonOptions)));
            }

            // Handouts — DMOnly (until revealed)
            foreach (var handout in clues.Handouts)
            {
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"Handout: {handout.Title}\n{handout.Content}",
                    sectionTitle: handout.Title,
                    chunkType: "handout",
                    visibility: ContentVisibility.DMOnly,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneId = handout.SceneId,
                    }, JsonOptions)));
            }

            // Fail-forward paths — DMOnly
            foreach (var path in clues.FailForwardPaths)
            {
                chunks.Add(new DocumentChunk(
                    sourceDocumentId: sourceDocumentId,
                    chunkIndex: chunkIndex++,
                    text: $"Fail-Forward: When {path.Trigger}, then {path.Resolution}",
                    sectionTitle: $"Fail-Forward ({path.SceneId})",
                    chunkType: "fail-forward",
                    visibility: ContentVisibility.DMOnly,
                    metadataJson: JsonSerializer.Serialize(new
                    {
                        adventureId = adventure.Id,
                        sceneId = path.SceneId,
                    }, JsonOptions)));
            }
        }

        return chunks;
    }

    private static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
