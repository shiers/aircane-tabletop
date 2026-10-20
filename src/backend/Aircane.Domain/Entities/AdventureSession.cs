using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// Runtime state of a player running through a <see cref="GeneratedAdventure"/>.
/// Separate from <see cref="Campaign"/> and <see cref="Session"/>: this is the adventure-specific
/// state layer (current scene, revealed clues, NPC and encounter outcomes) that sits alongside the
/// campaign state snapshot. Scene, NPC, clue and secret identifiers refer to content inside the
/// adventure's JSON columns, so they are stored as plain strings (no foreign keys).
/// </summary>
public class AdventureSession
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The generated adventure being played.</summary>
    public Guid GeneratedAdventureId { get; set; }

    /// <summary>The campaign this run belongs to.</summary>
    public Guid CampaignId { get; set; }

    /// <summary>
    /// The play <see cref="Session"/> used as the SignalR group for live adventure events.
    /// Null when no live session is attached (state is then only available via REST).
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>The character being played. Null when solo play uses no formal character.</summary>
    public Guid? CharacterId { get; set; }

    /// <summary>ID of the scene the player is in now.</summary>
    public string CurrentSceneId { get; set; } = string.Empty;

    /// <summary>IDs of scenes that have been fully completed, in completion order.</summary>
    public List<string> CompletedSceneIds { get; set; } = [];

    /// <summary>Lifecycle status of this run.</summary>
    public AdventureSessionStatus Status { get; set; } = AdventureSessionStatus.NotStarted;

    /// <summary>Free text written by the AI when the adventure ends.</summary>
    public string? OutcomeNotes { get; set; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the run was completed or abandoned. Null while in progress.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When the run last changed. Used to order and resume runs.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// What the AI last asked the player to roll (e.g. "Dexterity (Stealth)"), until the next player
    /// action is processed (or, in a group run, until the turn passes). Lets the AI treat "I rolled a
    /// 14" as the answer without asking again.
    /// </summary>
    public string? PendingRollLabel { get; set; }

    /// <summary>The difficulty class of the pending roll, when the AI gave one.</summary>
    public int? PendingRollDC { get; set; }

    /// <summary>One entry per NPC in the adventure.</summary>
    public List<AdventureNpcState> NpcStates { get; set; } = [];

    /// <summary>Clues (handouts) the player has discovered.</summary>
    public List<string> RevealedClueIds { get; set; } = [];

    /// <summary>Secrets the player has discovered.</summary>
    public List<string> RevealedSecretIds { get; set; } = [];

    /// <summary>How each encounter in the adventure was resolved.</summary>
    public List<AdventureEncounterOutcome> EncounterOutcomes { get; set; } = [];

    // ── Group play ────────────────────────────────────────────────────────────
    // Solo runs leave these at their defaults: IsGroup false, no turn order, no active participant.

    /// <summary>False for solo play (the default), true when several players take turns.</summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// <see cref="SessionParticipant"/> IDs in the order they take their turns. Empty for solo runs.
    /// </summary>
    public List<Guid> TurnOrder { get; set; } = [];

    /// <summary>The participant who may act right now. Null for solo runs.</summary>
    public Guid? ActiveParticipantId { get; set; }

    /// <summary>
    /// The round of play: starts at 1 for a group run and goes up each time the turn passes from the
    /// last participant back to the first. 0 for solo runs.
    /// </summary>
    public int CurrentRound { get; set; }

    /// <summary>
    /// How the turn moves on (group runs only): <see cref="TurnAdvanceMode.ManualOnly"/> by default.
    /// </summary>
    public TurnAdvanceMode TurnAdvance { get; set; } = TurnAdvanceMode.ManualOnly;
}

/// <summary>
/// Runtime state of one NPC within an <see cref="AdventureSession"/>.
/// </summary>
public class AdventureNpcState
{
    /// <summary>Stable NPC identifier derived from the NPC's name.</summary>
    public string NpcId { get; set; } = string.Empty;

    /// <summary>Display name of the NPC.</summary>
    public string NpcName { get; set; } = string.Empty;

    /// <summary>Whether the player has met this NPC.</summary>
    public bool HasBeenMet { get; set; }

    /// <summary>Whether the NPC is still alive.</summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>Which dialogue/info hooks have been revealed.</summary>
    public List<string> RevealedInfoIds { get; set; } = [];

    /// <summary>Anything the AI has surfaced about this NPC.</summary>
    public string? PlayerNotes { get; set; }
}

/// <summary>
/// How an encounter was resolved within an <see cref="AdventureSession"/>.
/// </summary>
public class AdventureEncounterOutcome
{
    /// <summary>Stable encounter identifier (<c>encounter-N</c>, by position in the adventure).</summary>
    public string EncounterId { get; set; } = string.Empty;

    /// <summary>One of "victory", "fled", "negotiated", "avoided".</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Optional free-text notes.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Lifecycle status of an <see cref="AdventureSession"/>.
/// </summary>
public enum AdventureSessionStatus
{
    /// <summary>Created but not yet started.</summary>
    NotStarted = 0,

    /// <summary>The player is running through the adventure.</summary>
    InProgress = 1,

    /// <summary>The adventure was finished.</summary>
    Completed = 2,

    /// <summary>The player gave up before finishing.</summary>
    Abandoned = 3,
}
