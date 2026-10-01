using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Adventures;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Adventures;

/// <summary>
/// Loads generated adventures from the database and deserializes JSON columns into DTOs.
/// </summary>
public sealed class AdventureRetrievalService : IAdventureRetrievalService
{
    private readonly AircaneDbContext _db;
    private readonly ILogger<AdventureRetrievalService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AdventureRetrievalService(AircaneDbContext db, ILogger<AdventureRetrievalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GeneratedAdventureDto?> GetByIdAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.GeneratedAdventures
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adventureId, cancellationToken);

        if (entity is null)
        {
            _logger.LogDebug("Adventure {AdventureId} not found", adventureId);
            return null;
        }

        return new GeneratedAdventureDto
        {
            Id = entity.Id,
            Status = entity.Status.ToString(),
            Request = DeserializeRequired<GenerateAdventureRequest>(entity.RequestJson, "request")!,
            PartyAnalysis = DeserializeOrNull<PartyAnalysisResult>(entity.PartyAnalysisJson, "partyAnalysis"),
            Pitch = DeserializeOrNull<AdventurePitch>(entity.PitchJson, "pitch"),
            Outline = DeserializeOrNull<AdventureOutline>(entity.OutlineJson, "outline"),
            Scenes = DeserializeOrNull<List<GeneratedScene>>(entity.ScenesJson, "scenes"),
            Npcs = DeserializeOrNull<List<GeneratedNpc>>(entity.NpcsJson, "npcs"),
            Encounters = DeserializeOrNull<List<GeneratedEncounter>>(entity.EncountersJson, "encounters"),
            Treasure = DeserializeOrNull<AdventureTreasure>(entity.TreasureJson, "treasure"),
            Clues = DeserializeOrNull<AdventureClues>(entity.CluesJson, "clues"),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    /// <inheritdoc />
    public async Task<AdventureDraftDto?> GetDraftAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.GeneratedAdventures
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adventureId, cancellationToken);

        if (entity is null)
        {
            _logger.LogDebug("Adventure {AdventureId} not found for draft retrieval", adventureId);
            return null;
        }

        return new AdventureDraftDto
        {
            Id = entity.Id,
            Title = entity.Title,
            Status = entity.Status.ToString(),
            Pitch = DeserializeOrNull<AdventurePitch>(entity.PitchJson, "pitch"),
            Outline = DeserializeOrNull<AdventureOutline>(entity.OutlineJson, "outline"),
            Scenes = DeserializeOrNull<List<GeneratedScene>>(entity.ScenesJson, "scenes"),
            Npcs = DeserializeOrNull<List<GeneratedNpc>>(entity.NpcsJson, "npcs"),
            Encounters = DeserializeOrNull<List<GeneratedEncounter>>(entity.EncountersJson, "encounters"),
            Treasure = DeserializeOrNull<AdventureTreasure>(entity.TreasureJson, "treasure"),
            Clues = DeserializeOrNull<AdventureClues>(entity.CluesJson, "clues"),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    // ── Deserialization Helpers ────────────────────────────────────────────────

    private T? DeserializeRequired<T>(string? json, string fieldName) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize {Field} JSON for adventure", fieldName);
            return null;
        }
    }

    private T? DeserializeOrNull<T>(string? json, string fieldName) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize {Field} JSON for adventure", fieldName);
            return null;
        }
    }
}
