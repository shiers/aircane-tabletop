namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Request for a player to join a session using an invite code. <paramref name="RejoinSecret"/> is the
/// browser's optional rejoin secret (base64url, at least 128 bits); only its hash is stored.
/// </summary>
public sealed record JoinSessionRequest(
    Guid SessionId,
    string DisplayName,
    string InviteCode,
    string? RejoinSecret = null);
