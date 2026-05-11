using Aircane.Application.DTOs.Dice;
using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Sessions;

// ── Client → Server ──────────────────────────────────────────────────────────

/// <summary>
/// Sent by a client to join a session group and announce their presence.
/// </summary>
public sealed record JoinSessionMessage(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    ParticipantRole Role);

/// <summary>
/// Sent by a client to broadcast a chat message to the session.
/// </summary>
public sealed record SendChatMessage(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Text);

/// <summary>
/// Sent by a player to describe their intended action for the AI DM loop.
/// </summary>
public sealed record SubmitPlayerActionMessage(
    Guid SessionId,
    Guid ParticipantId,
    string ActionText,
    Guid? CharacterId = null);

/// <summary>
/// Sent by a client to request a dice roll via the hub.
/// </summary>
public sealed record RollDiceMessage(
    Guid SessionId,
    Guid ParticipantId,
    string Formula,
    Guid? CharacterId = null,
    string? Context = null,
    RollVisibility Visibility = RollVisibility.Public);

/// <summary>
/// Sent by a client to record a physical dice result.
/// </summary>
public sealed record EnterManualRollMessage(
    Guid SessionId,
    Guid ParticipantId,
    int Value,
    Guid? CharacterId = null,
    string? Formula = null,
    string? Context = null,
    RollVisibility Visibility = RollVisibility.Public);

/// <summary>
/// Sent by a client to ask a rules question grounded in indexed sources.
/// </summary>
public sealed record RequestRulesLookupMessage(
    Guid SessionId,
    Guid ParticipantId,
    string Question);

/// <summary>
/// Sent by the host to approve a pending AI proposal.
/// </summary>
public sealed record ApproveAIProposalMessage(
    Guid SessionId,
    Guid ProposalId,
    Guid ParticipantId);

/// <summary>
/// Sent by the host to reject a pending AI proposal.
/// </summary>
public sealed record RejectAIProposalMessage(
    Guid SessionId,
    Guid ProposalId,
    Guid ParticipantId,
    string? Reason = null);

// ── Server → Client ──────────────────────────────────────────────────────────

/// <summary>
/// Broadcast when a participant joins the session.
/// </summary>
public sealed record ParticipantJoinedNotification(
    Guid SessionId,
    ParticipantDto Participant);

/// <summary>
/// Broadcast when a participant leaves or disconnects from the session.
/// </summary>
public sealed record ParticipantLeftNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName);

/// <summary>
/// Broadcast when a chat message is received.
/// </summary>
public sealed record ChatMessageReceivedNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Text,
    DateTimeOffset SentAt);

/// <summary>
/// Broadcast when a roll is recorded (public rolls go to all; private rolls go to roller + host).
/// </summary>
public sealed record RollRecordedNotification(
    Guid SessionId,
    RollDto Roll);

/// <summary>
/// Broadcast when the AI or host requests a roll from a specific participant.
/// </summary>
public sealed record RollRequestedNotification(
    Guid SessionId,
    Guid TargetParticipantId,
    Guid? CharacterId,
    string Formula,
    string Label,
    int? Dc,
    string? Reason);

/// <summary>
/// Broadcast when the AI begins streaming a narration.
/// </summary>
public sealed record AINarrationStartedNotification(
    Guid SessionId,
    Guid NarrationId);

/// <summary>
/// Broadcast for each streamed chunk of AI narration text.
/// </summary>
public sealed record AINarrationChunkNotification(
    Guid SessionId,
    Guid NarrationId,
    string Chunk);

/// <summary>
/// Broadcast when AI narration streaming is complete.
/// </summary>
public sealed record AINarrationCompletedNotification(
    Guid SessionId,
    Guid NarrationId,
    string FullText);

/// <summary>
/// Broadcast when the AI creates a new proposal awaiting host approval.
/// </summary>
public sealed record AIProposalCreatedNotification(
    Guid SessionId,
    Guid ProposalId,
    string ActionType,
    string Description,
    string ProposalJson);

/// <summary>
/// Broadcast when campaign state is updated (e.g. HP change, condition applied).
/// </summary>
public sealed record StateUpdatedNotification(
    Guid SessionId,
    string StateJson,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Broadcast when the active scene changes.
/// </summary>
public sealed record SceneChangedNotification(
    Guid SessionId,
    Guid SceneId,
    string SceneName,
    string? PublicDescription);

/// <summary>
/// Broadcast when the host or AI reveals a handout or hidden content to players.
/// </summary>
public sealed record HandoutRevealedNotification(
    Guid SessionId,
    Guid ContentId,
    string ContentType,
    string Title,
    string Content);

/// <summary>
/// Broadcast when the active combat turn advances.
/// </summary>
public sealed record CombatTurnChangedNotification(
    Guid SessionId,
    Guid ActiveCreatureId,
    string ActiveCreatureName,
    int Round,
    int TurnIndex);

/// <summary>
/// Broadcast when a background document import job changes status.
/// </summary>
public sealed record ImportStatusUpdatedNotification(
    Guid SessionId,
    Guid DocumentId,
    string Status,
    int? PercentComplete,
    string? ErrorMessage);
