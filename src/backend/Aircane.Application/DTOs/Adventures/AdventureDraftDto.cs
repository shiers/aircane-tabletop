namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Draft sections of a generated adventure for review.
/// Contains only the content sections without full request/party metadata.
/// </summary>
public sealed record AdventureDraftDto
{
    /// <summary>Unique identifier for this generated adventure.</summary>
    public required Guid Id { get; init; }

    /// <summary>Adventure title.</summary>
    public required string Title { get; init; }

    /// <summary>Current status of the generation pipeline.</summary>
    public required string Status { get; init; }

    /// <summary>Adventure pitch: title, hook, and summary.</summary>
    public AdventurePitch? Pitch { get; init; }

    /// <summary>High-level outline of scenes.</summary>
    public AdventureOutline? Outline { get; init; }

    /// <summary>Detailed scene descriptions with connections.</summary>
    public IReadOnlyList<GeneratedScene>? Scenes { get; init; }

    /// <summary>NPCs generated for the adventure.</summary>
    public IReadOnlyList<GeneratedNpc>? Npcs { get; init; }

    /// <summary>Encounters generated for the adventure.</summary>
    public IReadOnlyList<GeneratedEncounter>? Encounters { get; init; }

    /// <summary>Treasure and rewards for the adventure.</summary>
    public AdventureTreasure? Treasure { get; init; }

    /// <summary>Clues, secrets, and fail-forward paths.</summary>
    public AdventureClues? Clues { get; init; }

    /// <summary>When the adventure was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the adventure was last updated.</summary>
    public DateTime UpdatedAt { get; init; }
}
