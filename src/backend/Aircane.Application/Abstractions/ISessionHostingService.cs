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
    /// <exception cref="KeyNotFoundException">The participant is not in the session.</exception>
    /// <exception cref="InvalidOperationException">The participant was denied; a denied request cannot be approved.</exception>
    Task<ParticipantDto> ApproveParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Denies a pending join request. The participant is marked denied (the record is kept, so the server
    /// can say why), dropped from the roster, and their token is revoked, so every later request with it is
    /// refused. Idempotent: denying someone already denied returns them unchanged. A denied person may join
    /// again with the invite code, which makes a new request (the invite code is for the session, not the person).
    /// </summary>
    /// <exception cref="KeyNotFoundException">The participant is not in the session.</exception>
    /// <exception cref="InvalidOperationException">
    /// The participant is already approved. Removing someone who is playing is a different action
    /// (they may hold the turn in an adventure) and is not supported here.
    /// </exception>
    Task<ParticipantDto> DenyParticipantAsync(
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
