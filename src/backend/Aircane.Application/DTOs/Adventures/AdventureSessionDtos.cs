using System.Text.Json.Serialization;
using Aircane.Application.DTOs.Ai;
using Aircane.Domain.Enums;

namespace Aircane.Application.DTOs.Adventures;

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for starting (or resuming) a solo run of a generated adventure.
/// </summary>
/// <param name="GeneratedAdventureId">The approved adventure to play.</param>
/// <param name="CampaignId">The campaign the run belongs to.</param>
/// <param name="CharacterId">Optional character; solo play does not require a formal character.</param>
public sealed record StartAdventureSessionRequest(
    Guid GeneratedAdventureId,
    Guid CampaignId,
    Guid? CharacterId = null);

/// <summary>
/// Request body for manually advancing to another scene (host override).
/// </summary>
/// <param name="TargetSceneId">A scene reachable from the current scene.</param>
/// <param name="TransitionNarration">Optional narration shown to the player for the transition.</param>
public sealed record AdvanceSceneRequest(
    string TargetSceneId,
    string? TransitionNarration = null);

/// <summary>
/// Request body for submitting a free-text player action to the AI DM for an adventure run.
/// </summary>
/// <param name="ActionText">What the player does or says (e.g. "I search the shelves").</param>
public sealed record AdventureSessionActionRequest(string ActionText);

/// <summary>
/// Request body for starting (or resuming) a group run of a generated adventure.
/// </summary>
/// <param name="GeneratedAdventureId">The approved adventure to play.</param>
/// <param name="CampaignId">The campaign the run belongs to.</param>
/// <param name="ParticipantIds">
/// Participants of the play session, in the order they take their turns. The first one acts first.
/// </param>
/// <param name="PlaySessionId">
/// Optional. The play session the run belongs to (the one the players joined). It must belong to the
/// campaign and not have ended. When omitted the campaign's most recent play session that has not ended is
/// used, which is only right if the host has one open at a time.
/// </param>
/// <param name="TurnAdvance">
/// Optional. How the turn moves on: <c>"ManualOnly"</c> (the default: the acting player says they are done, or
/// the host passes it) or <c>"AfterEachAction"</c> (it also moves to the next player once the AI has answered the
/// acting player's action, unless the AI is waiting for that player to report a roll). Ignored when the call
/// resumes a run that is already going: the run keeps the setting it started with.
/// </param>
public sealed record StartGroupAdventureSessionRequest(
    Guid GeneratedAdventureId,
    Guid CampaignId,
    IReadOnlyList<Guid> ParticipantIds,
    Guid? PlaySessionId = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TurnAdvanceMode? TurnAdvance = null);

/// <summary>
/// Request body for adding a participant to the end of a group run's turn order.
/// </summary>
/// <param name="ParticipantId">A participant of the run's play session.</param>
public sealed record AddAdventureParticipantRequest(Guid ParticipantId);

// ── Responses ────────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight view of an <see cref="Aircane.Domain.Entities.AdventureSession"/> row.
/// </summary>
public sealed record AdventureSessionDto(
    Guid Id,
    Guid GeneratedAdventureId,
    Guid CampaignId,
    Guid? SessionId,
    Guid? CharacterId,
    string CurrentSceneId,
    string Status,
    string? OutcomeNotes,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastActivityAt);

/// <summary>A short "story so far" reminder shown when a run is resumed.</summary>
public sealed record AdventureRecapDto(string Recap);

/// <summary>One line of a run's saved conversation.</summary>
/// <param name="EntryType">"PlayerAction" or "AiNarration".</param>
public sealed record AdventureHistoryEntryDto(
    Guid Id,
    string EntryType,
    string Content,
    string? SceneId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Progress of an in-progress run, shown on an adventure's card so it can be resumed.
/// </summary>
/// <param name="SessionId">The run's ID (what to open to resume).</param>
public sealed record AdventureActiveSessionDto(
    Guid SessionId,
    int CurrentSceneNumber,
    int TotalScenes,
    string Status);

/// <summary>An exit from a scene: the scene it leads to.</summary>
public sealed record AdventureExitDto(string SceneId, string Title, string SceneType);

/// <summary>The state of an NPC within a run, plus the static fields needed to display it.</summary>
public sealed record AdventureNpcStateDto(
    string NpcId,
    string NpcName,
    string Role,
    bool HasBeenMet,
    bool IsAlive,
    IReadOnlyList<string> RevealedInfoIds,
    string? PlayerNotes);

/// <summary>A clue or secret the player has discovered.</summary>
public sealed record AdventureRevealedItemDto(string Id, string Name, string Description);

/// <summary>How an encounter was resolved.</summary>
public sealed record AdventureEncounterOutcomeDto(string EncounterId, string Title, string Outcome, string? Notes);

/// <summary>
/// The scene the player is currently in. Player-safe: DM notes and undiscovered secrets are
/// deliberately not included.
/// </summary>
public sealed record AdventureSceneDto(
    string SceneId,
    string Title,
    string Description,
    string SceneType,
    string? ReadAloudText,
    IReadOnlyList<string> Objectives,
    IReadOnlyList<AdventureExitDto> Exits,
    IReadOnlyList<AdventureNpcStateDto> Npcs);

/// <summary>One participant's place in a group run's turn order.</summary>
public sealed record ParticipantTurnDto(Guid ParticipantId, string ParticipantName, bool IsActive);

/// <summary>
/// Full current state of an adventure run. This is what the frontend loads and keeps in sync via
/// the adventure SignalR events. The group-play fields at the end are false/null/0/empty for solo runs.
/// </summary>
public sealed record AdventureSessionStateDto(
    Guid Id,
    Guid GeneratedAdventureId,
    Guid CampaignId,
    Guid? SessionId,
    Guid? CharacterId,
    string AdventureTitle,
    string Status,
    string? OutcomeNotes,
    AdventureSceneDto CurrentScene,
    int CurrentSceneNumber,
    int TotalScenes,
    int CompletedSceneCount,
    IReadOnlyList<string> CompletedSceneIds,
    IReadOnlyList<AdventureNpcStateDto> NpcStates,
    IReadOnlyList<AdventureRevealedItemDto> RevealedClues,
    IReadOnlyList<AdventureRevealedItemDto> RevealedSecrets,
    IReadOnlyList<AdventureEncounterOutcomeDto> EncounterOutcomes,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastActivityAt,
    bool IsGroup,
    Guid? ActiveParticipantId,
    string? ActiveParticipantName,
    int CurrentRound,
    IReadOnlyList<ParticipantTurnDto> TurnOrder,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TurnAdvanceMode TurnAdvance = TurnAdvanceMode.ManualOnly);

/// <summary>
/// What happened to one action the AI DM proposed. Names rather than enum numbers, so clients
/// can match on <c>"AdvanceScene"</c> / <c>"PermissionDenied"</c> directly.
/// </summary>
/// <param name="ActionType">The <c>AiActionType</c> name, e.g. "AdvanceScene" or "RequestRoll".</param>
/// <param name="Outcome">"Applied", "QueuedForApproval", "ValidationFailed" or "PermissionDenied".</param>
/// <param name="ErrorMessage">Why the action was not applied; null when it was.</param>
public sealed record AdventureActionOutcomeDto(string ActionType, string Outcome, string? ErrorMessage)
{
    /// <summary>Maps the AI loop's per-action result to the client-facing shape.</summary>
    public static AdventureActionOutcomeDto From(ProposedActionResult result)
        => new(result.ActionType.ToString(), result.Outcome.ToString(), result.ErrorMessage);
}

/// <summary>
/// Response to a player action in an adventure run: the AI DM's narration, what happened to the
/// actions it proposed, and the refreshed run state.
/// <para>
/// The Host gets all of it. A Player gets only <see cref="Narration"/>: the private DM note is null, the
/// action outcomes are empty and <see cref="State"/> is null, because the full run state and the AI's
/// proposed actions carry more than a player may see. A Player reads the table's view from
/// <c>GET /api/sessions/{id}/adventure-run</c>.
/// </para>
/// </summary>
public sealed record AdventureSessionActionResponse(
    string Narration,
    string? PrivateDmNote,
    IReadOnlyList<AdventureActionOutcomeDto> Actions,
    AdventureSessionStateDto? State);

/// <summary>
/// The player-safe view of an adventure run: what a Player at the table may be told. Everything here is
/// something the table already knows or the acting player was shown.
/// <para>
/// <b>Included:</b> the adventure title, the current scene's title and read-aloud text, how far through
/// the adventure it is, the acting participant, the turn order, the round, a pending roll (only to the
/// player who owes it), and the recent public narration (what the AI DM said to the table and what
/// players typed).
/// </para>
/// <para>
/// <b>Excluded:</b> DM notes, the scene's full description (a synopsis that can spoil what happens),
/// hidden secrets and clues, NPC details, encounters, exits and objectives, the AI's private DM note
/// (it is never stored), and internal ids (adventure, campaign, character).
/// </para>
/// </summary>
public sealed record PlayerAdventureRunDto(
    Guid RunId,
    string AdventureTitle,
    string Status,
    bool IsGroup,
    PlayerSceneDto Scene,
    int CurrentSceneNumber,
    int TotalScenes,
    Guid? ActiveParticipantId,
    string? ActiveParticipantName,
    int CurrentRound,
    IReadOnlyList<ParticipantTurnDto> TurnOrder,
    PlayerPendingRollDto? PendingRoll,
    IReadOnlyList<PlayerNarrationEntryDto> RecentNarration,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TurnAdvanceMode TurnAdvance = TurnAdvanceMode.ManualOnly);

/// <summary>The current scene as a Player sees it: its title and the text meant to be read to the table.</summary>
/// <param name="Description">The read-aloud text; null when the scene has none.</param>
public sealed record PlayerSceneDto(string Title, string? Description);

/// <summary>A roll the AI DM asked the acting player for and that has not been answered yet.</summary>
public sealed record PlayerPendingRollDto(string Label, int? Dc);

/// <summary>One line of the public story so far.</summary>
/// <param name="Kind">"PlayerAction" (what a player typed) or "AiNarration" (what the AI DM narrated).</param>
public sealed record PlayerNarrationEntryDto(string Kind, string Text, DateTimeOffset CreatedAt);
