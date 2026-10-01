namespace Aircane.Application.DTOs.Sessions;

/// <summary>
/// Optional request body for ending a session.
/// The summary is saved to the session record and can be used for campaign continuity.
/// </summary>
public sealed record EndSessionRequest(
    /// <summary>
    /// Optional narrative summary of the session (e.g. what happened, unresolved threads).
    /// </summary>
    string? Summary = null);
