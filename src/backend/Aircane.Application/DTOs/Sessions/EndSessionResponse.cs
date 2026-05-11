namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Response returned after a session is successfully ended.
/// </summary>
public sealed record EndSessionResponse(
    Guid SessionId,
    DateTimeOffset EndedAt,
    string? Summary);
