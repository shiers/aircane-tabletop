using Aircane.Application.DTOs.Ai;

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

/// <summary>
/// Full current state of an adventure run. This is what the frontend loads and keeps in sync via
/// the adventure SignalR events.
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
    DateTimeOffset LastActivityAt);

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
/// </summary>
public sealed record AdventureSessionActionResponse(
    string Narration,
    string? PrivateDmNote,
    IReadOnlyList<AdventureActionOutcomeDto> Actions,
    AdventureSessionStateDto State);
