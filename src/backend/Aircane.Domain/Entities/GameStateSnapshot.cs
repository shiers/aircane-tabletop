using Aircane.Domain.Common;

namespace Aircane.Domain.Entities;

/// <summary>
/// A cached snapshot of the current campaign state for fast loading.
/// Stores the full game state as JSON: current scene, party resources,
/// world flags, revealed content, NPC states, encounters, and clues.
/// </summary>
public class GameStateSnapshot : EntityBase
{
    public Guid CampaignId { get; init; }
    public Guid? ActiveSessionId { get; set; }
    public string? CurrentSceneId { get; set; }
    public string StateJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GameStateSnapshot(
        Guid campaignId,
        string stateJson,
        Guid? activeSessionId = null,
        string? currentSceneId = null)
    {
        CampaignId = campaignId;
        StateJson = stateJson;
        ActiveSessionId = activeSessionId;
        CurrentSceneId = currentSceneId;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private GameStateSnapshot() : base()
    {
        StateJson = "{}";
    }
}
