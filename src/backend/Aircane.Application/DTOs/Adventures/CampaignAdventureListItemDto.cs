namespace Aircane.Application.DTOs.Adventures;

/// <summary>
/// One adventure on a campaign's page: enough to render a card and pick its actions.
/// </summary>
public sealed record CampaignAdventureListItemDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Draft, Approved, Active or Rejected.</summary>
    public required string Status { get; init; }

    public required string GameSystem { get; init; }

    public required string Ruleset { get; init; }

    /// <summary>"Solo" or "Group"; null if the stored request could not be read.</summary>
    public string? Mode { get; init; }

    /// <summary>"one-shot", "short", ...; null if the stored request could not be read.</summary>
    public string? Length { get; init; }

    public int? AverageLevel { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>The in-progress run of this adventure in this campaign, or null if there is none.</summary>
    public AdventureActiveSessionDto? ActiveSession { get; init; }
}
