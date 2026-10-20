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
    /// Takes a seat back with the rejoin secret the browser sent when it joined: the same participant, with its
    /// approval (or its place in the queue), character and turn position, and a fresh token. The previous token
    /// is revoked, so a seat has one live login. No new join request is made and nothing is announced.
    /// <see cref="RejoinOutcome.Denied"/> for a request the host declined (no token);
    /// <see cref="RejoinOutcome.NotFound"/> for anything else: an unknown or malformed secret, an ended session,
    /// or no such session.
    /// </summary>
    Task<RejoinAttempt> RejoinAsync(
        Guid sessionId,
        string? secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="RejoinAsync"/> would give back, without minting a token: the session's name, the
    /// participant's name and status (approved, pending or denied). <see cref="RejoinOutcome.NotFound"/> on the
    /// same terms.
    /// </summary>
    Task<RejoinStatusAttempt> GetRejoinStatusAsync(
        Guid sessionId,
        string? secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives an approved participant's seat to a pending join request (the same person on a new device or
    /// tab): approves the new participant and moves the old one's character and turn-order position to it.
    /// If the old participant holds the turn, the new one holds it now, and a pending roll is cleared (it was
    /// asked of the old connection; the AI asks again). The old participant is taken off the roster, its token
    /// and rejoin secret are revoked and its connections closed.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Either participant is not in the session.</exception>
    /// <exception cref="InvalidOperationException">
    /// The new participant is already approved or was denied, the old one has no seat (pending, denied or
    /// already replaced), or both are the same participant.
    /// </exception>
    Task<ReplaceParticipantResult> ReplaceParticipantAsync(
        Guid sessionId,
        Guid oldParticipantId,
        Guid newParticipantId,
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
    /// Returns one participant of a session, or null when there is no such participant in it. Unlike
    /// <see cref="GetParticipantsAsync"/> this also finds a participant the host denied
    /// (<see cref="ParticipantDto.IsDenied"/>), so an access check can tell "denied" from "never joined".
    /// </summary>
    Task<ParticipantDto?> GetParticipantAsync(
        Guid sessionId,
        Guid participantId,
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
