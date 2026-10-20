using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

/// <summary>Where a participant stands, as the rejoin endpoints say it.</summary>
public static class RejoinStatus
{
    public const string Approved = "approved";
    public const string Pending = "pending";
    public const string Denied = "denied";
}

/// <summary>
/// A seat taken back with the rejoin secret: the same participant (approval, character and turn position
/// unchanged) and a fresh participant token. The secret is never returned.
/// </summary>
public sealed record RejoinSessionResult(
    Guid ParticipantId,
    string DisplayName,
    ParticipantRole Role,
    bool IsApproved,
    string Status,
    Guid? CharacterId,
    string SessionName,
    string ParticipantToken);

/// <summary>
/// What the rejoin secret would give back, without logging anyone in: the session's name, the participant's
/// display name and their status (<see cref="RejoinStatus"/>). No token.
/// </summary>
public sealed record RejoinStatusResult(
    string SessionName,
    string DisplayName,
    string Status);

/// <summary>The outcome of a rejoin attempt.</summary>
public enum RejoinOutcome
{
    /// <summary>The secret matched a participant with a seat (approved or still waiting).</summary>
    Found,

    /// <summary>The secret matched a participant the host declined. No token is minted.</summary>
    Denied,

    /// <summary>
    /// Nothing to give back: an unknown or malformed secret, a session that has ended, or no such session.
    /// Deliberately one outcome, so the response does not say which.
    /// </summary>
    NotFound,
}

/// <summary>A rejoin attempt: <see cref="Result"/> is set only for <see cref="RejoinOutcome.Found"/>.</summary>
public sealed record RejoinAttempt(RejoinOutcome Outcome, RejoinSessionResult? Result = null)
{
    public static readonly RejoinAttempt NotFound = new(RejoinOutcome.NotFound);
    public static readonly RejoinAttempt Denied = new(RejoinOutcome.Denied);
}

/// <summary>A status check: <see cref="Result"/> is set unless the outcome is <see cref="RejoinOutcome.NotFound"/>.</summary>
public sealed record RejoinStatusAttempt(RejoinOutcome Outcome, RejoinStatusResult? Result = null)
{
    public static readonly RejoinStatusAttempt NotFound = new(RejoinOutcome.NotFound);
}

/// <summary>The outcome of giving an old participant's seat to a newer join request.</summary>
public sealed record ReplaceParticipantResult(
    ParticipantDto Participant,
    Guid ReplacedParticipantId,
    IReadOnlyList<Guid> AdventureRunIds,
    bool TookTheTurn);
