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
/// Broadcast when a chat message is received. <paramref name="MessageId"/> is the stored message's id, the
/// same one <c>GET /api/sessions/{id}/chat</c> returns, so a client that loaded history can tell it already has it.
/// </summary>
public sealed record ChatMessageReceivedNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    string Text,
    DateTimeOffset SentAt,
    Guid? MessageId = null,
    string? Role = null);

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
    // Combatant IDs are strings: player characters use their Character Guid as a string,
    // NPCs/monsters use a stable slug. Null when no combatant is active (e.g. before initiative).
    string? ActiveCreatureId,
    string? ActiveCreatureName,
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

// ── Solo adventure events ────────────────────────────────────────────────────
// For these notifications SessionId is the ADVENTURE session id (the run), not the play session
// whose SignalR group the event is delivered to. Clients filter on it to ignore other runs.

/// <summary>
/// Broadcast when an adventure run moves to a new scene (AI-driven or host override).
/// </summary>
public sealed record AdventureSceneAdvancedNotification(
    Guid SessionId,
    string PreviousSceneId,
    string NewSceneId,
    string? Narration);

/// <summary>
/// Broadcast when the player discovers a clue (handout).
/// </summary>
public sealed record AdventureClueRevealedNotification(
    Guid SessionId,
    string ClueId,
    string ClueName,
    string Description);

/// <summary>
/// Broadcast when the player discovers a secret.
/// </summary>
public sealed record AdventureSecretRevealedNotification(
    Guid SessionId,
    string SecretId,
    string SecretName,
    string Description);

/// <summary>
/// Broadcast when an NPC's state changes (met, killed, new info revealed).
/// </summary>
public sealed record AdventureNpcUpdatedNotification(
    Guid SessionId,
    string NpcId,
    string NpcName,
    bool HasBeenMet,
    bool IsAlive);

/// <summary>
/// Broadcast when the adventure run is completed.
/// </summary>
public sealed record AdventureCompletedNotification(
    Guid SessionId,
    string OutcomeNotes);

// ── Group adventure events ───────────────────────────────────────────────────
// SessionId is the ADVENTURE session id here too.

/// <summary>
/// Broadcast when the host passes the turn to the next participant of a group run.
/// </summary>
/// <param name="PreviousParticipantId">Who had the turn before; null if no one did.</param>
/// <param name="CurrentRound">The round now in progress (goes up when the turn wraps to the first player).</param>
/// <param name="PassedBy">Who moved the turn on: <c>"Host"</c>, <c>"Player"</c> (the acting player ended it) or <c>"Auto"</c>.</param>
public sealed record TurnAdvancedNotification(
    Guid SessionId,
    Guid? PreviousParticipantId,
    Guid ActiveParticipantId,
    string ActiveParticipantName,
    int CurrentRound,
    string? PassedBy = null);

/// <summary>
/// Broadcast when a participant is added to the end of a group run's turn order (SignalR event
/// <c>ParticipantJoinedAdventure</c>). <c>turnPosition</c> is zero-based: the first participant is 0, so it
/// is the index into the state DTO's <c>turnOrder</c> array.
/// </summary>
/// <param name="TurnPosition">Zero-based index of the new participant in the run's turn order (0 = first to act).</param>
public sealed record ParticipantJoinedAdventureNotification(
    Guid SessionId,
    Guid ParticipantId,
    string ParticipantName,
    int TurnPosition);

// ── Lobby events ─────────────────────────────────────────────────────────────
// Broadcast to the play session's group. None of these carries an invite code, a token or anything
// else the recipient could use to act as someone: only names and ids already shown in the roster.

/// <summary>
/// Broadcast when the host approves a join request, or when a joiner is approved on joining because the
/// session does not require approval (SignalR event <c>ParticipantApproved</c>).
/// </summary>
/// <param name="AutoApproved">True when the session approved the joiner itself (no host action).</param>
public sealed record ParticipantApprovedNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    bool AutoApproved);

/// <summary>Broadcast when the host denies a join request (SignalR event <c>ParticipantDenied</c>).</summary>
public sealed record ParticipantDeniedNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName);

/// <summary>
/// Sent to the host and to the person removed when the host takes an approved participant out of the session
/// (SignalR event <c>ParticipantRemoved</c>), just before that person's connections are closed. The table hears
/// <c>ParticipantLeft</c> instead.
/// </summary>
public sealed record ParticipantRemovedNotification(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName);

/// <summary>
/// Sent to the host only when a participant's first hub connection opens or their last one closes (SignalR event
/// <c>ParticipantPresenceChanged</c>). Ids and a flag only: never a connection id or a token.
/// </summary>
public sealed record ParticipantPresenceChangedNotification(
    Guid SessionId,
    Guid ParticipantId,
    bool IsConnected);

/// <summary>
/// Broadcast when something about an adventure run that players can see has changed (SignalR event
/// <c>AdventureStateChanged</c>): the turn passed, the scene changed, or a player action was answered.
/// It is deliberately only a signal. It carries no scene, narration or notes, so it can never leak
/// hidden content; clients react by reading <c>GET /api/sessions/{id}/adventure-run</c>.
/// <c>SessionId</c> is the ADVENTURE session id (the run), as in the other adventure events.
/// </summary>
/// <param name="Reason">"TurnAdvanced", "SceneAdvanced" or "PlayerAction".</param>
public sealed record AdventureStateChangedNotification(
    Guid SessionId,
    string Reason,
    DateTimeOffset ChangedAt);
