namespace Aircane.Application.DTOs.Ai;

/// <summary>
/// Request to process a player action through the AI DM pipeline.
/// </summary>
public sealed record PlayerActionRequest(
    /// <summary>The session in which the action is being taken.</summary>
    Guid SessionId,

    /// <summary>The campaign this session belongs to.</summary>
    Guid CampaignId,

    /// <summary>The character performing the action.</summary>
    Guid CharacterId,

    /// <summary>The player's action text (e.g. "I try to sneak past the guards").</summary>
    string ActionText);
