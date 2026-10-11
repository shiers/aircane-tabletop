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

    /// <summary>One entry per NPC in the adventure.</summary>
    public List<AdventureNpcState> NpcStates { get; set; } = [];

    /// <summary>Clues (handouts) the player has discovered.</summary>
    public List<string> RevealedClueIds { get; set; } = [];

    /// <summary>Secrets the player has discovered.</summary>
    public List<string> RevealedSecretIds { get; set; } = [];

    /// <summary>How each encounter in the adventure was resolved.</summary>
    public List<AdventureEncounterOutcome> EncounterOutcomes { get; set; } = [];
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
