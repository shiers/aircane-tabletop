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
public sealed record AdventureContext(Guid SessionId, string Text);
