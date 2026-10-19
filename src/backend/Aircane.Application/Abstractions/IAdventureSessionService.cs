using Aircane.Application.DTOs.Adventures;
using Aircane.Domain.Entities;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Runs a player through a <see cref="GeneratedAdventure"/> scene by scene: tracks the current
/// scene, discovered clues and secrets, and NPC state, and records every change in the campaign
/// event log. Methods throw <see cref="KeyNotFoundException"/> for unknown runs/adventures/campaigns
/// and <see cref="InvalidOperationException"/> for rule violations (unreachable scene, unknown
/// clue ID, run no longer in progress).
/// </summary>
public interface IAdventureSessionService
{
    /// <summary>
    /// Starts a run of the given adventure in a campaign. Idempotent: if the campaign already has an
    /// in-progress run of the same adventure, that run is returned (resuming is the same as starting).
    /// </summary>
    /// <exception cref="KeyNotFoundException">The adventure or campaign does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// The adventure is not approved, has no scenes, or the campaign already has a different adventure in progress.
    /// </exception>
    Task<AdventureSession> StartAdventureSessionAsync(
        Guid generatedAdventureId,
        Guid campaignId,
        Guid? characterId,
        CancellationToken ct = default);

    /// <summary>
    /// Starts a group run: several participants of a play session take turns, in
    /// <paramref name="participantIds"/> order, with the first one acting first. The run uses that play
    /// session as its SignalR group. The play session is <paramref name="playSessionId"/> when given
    /// (it must belong to the campaign and not be ended); otherwise the campaign's most recent play
    /// session that has not ended. Idempotent: if the campaign already has an in-progress group run of
    /// the same adventure, that run is returned.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The adventure, campaign or given play session does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// The adventure is not approved, has no scenes, no participants were given (or one is listed twice
    /// or is not an active participant of the play session), the given play session belongs to another
    /// campaign or has ended, or the campaign already has a different adventure, or a solo run, in progress.
    /// </exception>
    Task<AdventureSession> StartGroupSessionAsync(
        Guid generatedAdventureId,
        Guid campaignId,
        IReadOnlyList<Guid> participantIds,
        Guid? playSessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// What a Player may see of the in-progress adventure run attached to a play session, or null when
    /// there is none. Contains no DM notes, hidden secrets, unrevealed content or DM-only AI notes; see
    /// <see cref="PlayerAdventureRunDto"/>. <paramref name="callerParticipantId"/> is the asking
    /// participant (null for the host), used to include a pending roll only for the player who owes it.
    /// </summary>
    Task<PlayerAdventureRunDto?> GetPlayerRunViewAsync(
        Guid playSessionId,
        Guid? callerParticipantId,
        CancellationToken ct = default);

    /// <summary>
    /// Passes the turn of a group run to the next participant in the turn order, wrapping from the
    /// last participant to the first (which starts the next round). Clears any pending roll: it was
    /// owed by the player whose turn just ended. Broadcasts <c>TurnAdvanced</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The run does not exist.</exception>
    /// <exception cref="InvalidOperationException">The run is not a group run, or is no longer in progress.</exception>
    Task<AdventureSession> AdvanceTurnAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Adds a participant of the run's play session to the end of a group run's turn order.
    /// Broadcasts <c>ParticipantJoinedAdventure</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The run does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// The run is not an in-progress group run, the participant is already in it, or is not an active
    /// participant of the run's play session.
    /// </exception>
    Task<AdventureSession> AddParticipantAsync(Guid sessionId, Guid participantId, CancellationToken ct = default);

    /// <summary>
    /// Attaches the play <c>Session</c> whose SignalR group receives this run's live events.
    /// </summary>
    Task AttachPlaySessionAsync(Guid sessionId, Guid playSessionId, CancellationToken ct = default);

    /// <summary>Returns a run by ID, or null if it does not exist.</summary>
    Task<AdventureSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns the campaign's in-progress run, or null if there is none.</summary>
    Task<AdventureSession?> GetActiveByCampaignAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full current state of a run: current scene (description, objectives, exits, NPCs),
    /// completed-scene count, revealed clues and secrets, NPC states and encounter outcomes.
    /// </summary>
    Task<AdventureSessionStateDto> GetSessionStateAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Returns the scenes reachable from the run's current scene.</summary>
    Task<IReadOnlyList<AdventureExitDto>> GetAvailableScenesAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Moves the run to <paramref name="targetSceneId"/>, which must exist in the adventure and be
    /// reachable from the current scene. The current scene is added to the completed scenes.
    /// </summary>
    Task<AdventureSession> AdvanceSceneAsync(
        Guid sessionId,
        string targetSceneId,
        string? transitionNarration,
        CancellationToken ct = default);

    /// <summary>Marks a clue (handout) as discovered. No-op if already revealed.</summary>
    Task RevealClueAsync(Guid sessionId, string clueId, CancellationToken ct = default);

    /// <summary>Marks a secret as discovered. No-op if already revealed.</summary>
    Task RevealSecretAsync(Guid sessionId, string secretId, CancellationToken ct = default);

    /// <summary>Patches an NPC's state; only arguments that are non-null are applied.</summary>
    Task UpdateNpcStateAsync(
        Guid sessionId,
        string npcId,
        bool? hasBeenMet,
        bool? isAlive,
        string? revealedInfoId,
        string? playerNotes,
        CancellationToken ct = default);

    /// <summary>
    /// Records the roll the AI just asked the player for, or clears it when <paramref name="label"/> is
    /// null. Called after every player action: the previous pending roll is consumed by that action,
    /// and a new one is set only if the AI asked for another roll.
    /// </summary>
    Task UpdatePendingRollAsync(Guid sessionId, string? label, int? dc, CancellationToken ct = default);

    /// <summary>
    /// Saves one exchange to the run's transcript: the player's action and the AI's narration.
    /// <paramref name="sceneIdAtStart"/> is the scene the player acted in; the narration is stamped with
    /// the scene the run is in once the AI's actions have been applied.
    /// </summary>
    Task RecordExchangeAsync(
        Guid sessionId,
        string playerAction,
        string narration,
        string? sceneIdAtStart,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a player comes back to a run. Clears any pending roll: a roll the AI asked for in an
    /// earlier visit and never got an answer to must not leak into this one.
    /// </summary>
    Task ResumeAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Asks the AI for a 2-3 sentence "story so far" reminder built only from the run's state (no
    /// retrieval, no transcript), to show when the player returns.
    /// </summary>
    Task<string> GenerateRecapAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Returns the last <paramref name="limit"/> transcript entries, oldest first.</summary>
    Task<IReadOnlyList<AdventureHistoryEntryDto>> GetHistoryAsync(
        Guid sessionId, int limit = 50, CancellationToken ct = default);

    /// <summary>Every run that is currently in progress, most recently active first.</summary>
    Task<IReadOnlyList<AdventureSession>> ListInProgressAsync(CancellationToken ct = default);

    /// <summary>Ends the run successfully.</summary>
    Task CompleteAdventureAsync(Guid sessionId, string outcomeNotes, CancellationToken ct = default);

    /// <summary>Ends the run early. Abandoning an already-abandoned run is a no-op.</summary>
    Task AbandonAdventureAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Renders the structured text block injected into the AI DM's system prompt: the current scene,
    /// objectives, exits, NPCs, what is discoverable here, and what the player already knows.
    /// </summary>
    string BuildAdventureContext(AdventureSession session, GeneratedAdventureDto adventure);

    /// <summary>
    /// Loads the campaign's in-progress run and renders its context block. Returns null when the
    /// campaign has no run in progress (or its adventure can no longer be loaded).
    /// </summary>
    Task<AdventureContext?> BuildActiveAdventureContextAsync(Guid campaignId, CancellationToken ct = default);
}

/// <summary>
/// The prompt context for a campaign's in-progress adventure run.
/// </summary>
/// <param name="SessionId">The adventure run's ID.</param>
/// <param name="Text">The context block to include in the AI system prompt.</param>
/// <param name="CurrentSceneId">The scene the run is in right now (null if unknown).</param>
/// <param name="IsGroup">True for a group run; selects the group instruction block instead of the solo one.</param>
/// <param name="PlayersWithoutCharacter">
/// Group runs only: names of the participants with no character assigned in this app, in turn order.
/// Null for solo runs.
/// </param>
public sealed record AdventureContext(
    Guid SessionId,
    string Text,
    string? CurrentSceneId = null,
    bool IsGroup = false,
    IReadOnlyList<string>? PlayersWithoutCharacter = null);
