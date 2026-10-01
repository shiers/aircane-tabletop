namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// Summary DTO for listing adventures. Contains only the fields needed
/// for browsing and managing adventures without full content.
/// </summary>
public sealed record AdventureListItemDto
{
    /// <summary>Unique identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Adventure title.</summary>
    public required string Title { get; init; }

    /// <summary>Current status (Draft, Approved, Active, Rejected).</summary>
    public required string Status { get; init; }

    /// <summary>Ruleset used for generation (e.g., "D&D 5e 2014").</summary>
    public required string Ruleset { get; init; }

    /// <summary>Game system (e.g., "D&D 5e 2014").</summary>
    public required string GameSystem { get; init; }

    /// <summary>When the adventure was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the adventure was last updated.</summary>
    public required DateTime UpdatedAt { get; init; }
}
