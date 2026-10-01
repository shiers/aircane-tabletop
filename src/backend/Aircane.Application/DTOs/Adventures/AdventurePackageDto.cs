namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Represents the exported adventure package structure.
/// Each property maps to a file in the package directory:
/// /generated-adventures/{adventureId}/
///   adventure.json
///   scenes.json
///   npcs.json
///   encounters.json
///   treasure.json
///   clues.json
///   session-notes.md
/// </summary>
public sealed record AdventurePackageDto
{
    /// <summary>Adventure metadata and pitch (adventure.json).</summary>
    public required AdventurePackageMetadata Adventure { get; init; }

    /// <summary>Scene graph (scenes.json).</summary>
    public IReadOnlyList<GeneratedScene>? Scenes { get; init; }

    /// <summary>NPCs (npcs.json).</summary>
    public IReadOnlyList<GeneratedNpc>? Npcs { get; init; }

    /// <summary>Encounters (encounters.json).</summary>
    public IReadOnlyList<GeneratedEncounter>? Encounters { get; init; }

    /// <summary>Treasure (treasure.json).</summary>
    public AdventureTreasure? Treasure { get; init; }

    /// <summary>Clues (clues.json).</summary>
    public AdventureClues? Clues { get; init; }

    /// <summary>Session notes markdown content (session-notes.md).</summary>
    public string SessionNotes { get; init; } = string.Empty;
}

/// <summary>
/// Adventure metadata for the adventure.json file in the package.
/// </summary>
public sealed record AdventurePackageMetadata
{
    /// <summary>Adventure ID.</summary>
    public required Guid Id { get; init; }

    /// <summary>Adventure title.</summary>
    public required string Title { get; init; }

    /// <summary>Current status.</summary>
    public required string Status { get; init; }

    /// <summary>Ruleset used for generation.</summary>
    public required string Ruleset { get; init; }

    /// <summary>Game system used for generation.</summary>
    public required string GameSystem { get; init; }

    /// <summary>Adventure pitch.</summary>
    public AdventurePitch? Pitch { get; init; }

    /// <summary>Adventure outline.</summary>
    public AdventureOutline? Outline { get; init; }

    /// <summary>When the adventure was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the adventure was last updated.</summary>
    public DateTime UpdatedAt { get; init; }
}
