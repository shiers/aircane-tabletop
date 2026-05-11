namespace Aircane.Application.DTOs.CampaignState;

/// <summary>
/// Request to apply a named command to the campaign state.
/// The payload is a JSON-serialized command object.
/// </summary>
public sealed record ApplyCommandRequest(
    Guid CampaignId,
    string CommandType,
    string PayloadJson,
    string ActorType,
    Guid? ActorId = null,
    Guid? SessionId = null);
