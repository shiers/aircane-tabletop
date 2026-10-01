namespace Aircane.Application.AiRuntime;

/// <summary>
/// Defines the capabilities that can be granted to an AI role.
/// Each capability represents a category of actions the AI is permitted to perform.
/// </summary>
public enum AiRoleCapability
{
    /// <summary>AI can provide suggestions to the DM (not visible to players unless shared).</summary>
    Suggestions,

    /// <summary>AI can perform rules lookups and answer rules questions.</summary>
    RulesLookup,

    /// <summary>AI can generate session summaries and recaps.</summary>
    Summaries,

    /// <summary>AI can provide DM-facing help (encounter prep, NPC notes, etc.).</summary>
    DmFacingHelp,

    /// <summary>AI can act as rules referee, adjudicating rules disputes.</summary>
    RulesReferee,

    /// <summary>AI can generate and voice NPC dialogue.</summary>
    NpcDialogue,

    /// <summary>AI can narrate scenes and descriptions to players.</summary>
    Narration,

    /// <summary>AI can assist with combat (initiative, tracking, suggestions).</summary>
    CombatSupport,

    /// <summary>AI can act as session scribe, recording events and notes.</summary>
    SessionScribe,

    /// <summary>AI can request dice rolls from players.</summary>
    RequestRolls,

    /// <summary>AI can reveal hidden scene content (handouts, clues, room descriptions).</summary>
    RevealContent,

    /// <summary>AI can run NPCs autonomously (actions, reactions, dialogue).</summary>
    RunNpcs,

    /// <summary>AI can propose state changes (damage, conditions, world flags, etc.).</summary>
    ProposeStateChanges,

    /// <summary>AI can override its role on a per-scene basis (Hybrid mode).</summary>
    SceneLevelOverride,
}
