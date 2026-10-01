using Aircane.Application.DTOs.Sessions;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Creates sessions, manages invite codes, participant roles, the join flow,
/// and the session connection lifecycle.
/// </summary>
public interface ISessionHostingService
{
    /// <summary>
    /// Creates a new session under a campaign and returns the session details
    /// along with the generated invite code (plain text, returned once only).
    /// </summary>
    Task<(SessionDto Session, string InviteCode)> CreateSessionAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a player join request. Returns the participant identity and
    /// a signed participant token. The participant may be pending approval.
    /// </summary>
    Task<JoinSessionResult> JoinSessionAsync(
        JoinSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending participant, allowing them to participate in the session.
    /// </summary>
    Task<ParticipantDto> ApproveParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a character to a participant in the session.
    /// </summary>
    Task<ParticipantDto> AssignCharacterAsync(
        Guid sessionId,
        Guid participantId,
        Guid characterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the session details and current participant list.
    /// </summary>
    Task<SessionDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all participants in a session.
    /// </summary>
    Task<IReadOnlyList<ParticipantDto>> GetParticipantsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session, saves an optional summary, and invalidates all participant tokens.
    /// </summary>
    Task<EndSessionResponse> EndSessionAsync(
        Guid sessionId,
        string? summary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses AI-initiated actions for the session.
    /// </summary>
    Task PauseAiAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes AI to its prior role and authority level as configured on the campaign.
    /// Returns the restored AI role and authority.
    /// </summary>
    Task<ResumeAiResponse> ResumeAiAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
