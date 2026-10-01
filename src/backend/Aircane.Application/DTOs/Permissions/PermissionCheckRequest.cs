namespace Aircane.Application.DTOs.Permissions;

/// <summary>
/// Request to check whether a participant is authorized to perform an action.
/// </summary>
public sealed record PermissionCheckRequest(
    Guid SessionId,
    Guid ParticipantId,
    string Action,
    string? ResourceType = null,
    Guid? ResourceId = null);
