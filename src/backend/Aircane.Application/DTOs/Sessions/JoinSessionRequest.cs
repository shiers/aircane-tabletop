namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Request for a player to join a session using an invite code.
/// </summary>
public sealed record JoinSessionRequest(
    Guid SessionId,
    string DisplayName,
    string InviteCode);
