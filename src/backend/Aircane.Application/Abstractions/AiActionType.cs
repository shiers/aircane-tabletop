namespace Aircane.Application.Abstractions;

/// <summary>
/// Enumerates the action types the AI may propose in a structured output response.
/// All state-changing actions must pass through server-side validation and authorization
/// before being applied to campaign state.
/// </summary>
public enum AiActionType
{
    /// <summary>Pure narration — no state change; always safe to display.</summary>
    Narrate,

    /// <summary>Ask a player or character to make a dice roll.</summary>
    RequestRoll,

    /// <summary>Apply hit-point damage to a character or creature.</summary>
    ApplyDamage,

    /// <summary>Apply hit-point healing to a character or creature.</summary>
    ApplyHealing,

    /// <summary>Apply a condition (e.g. Poisoned, Prone) to a character or creature.</summary>
    ApplyCondition,

    /// <summary>Remove a condition from a character or creature.</summary>
    RemoveCondition,

    /// <summary>Reveal hidden adventure content (handout, clue, room description) to players.</summary>
    RevealContent,

    /// <summary>Move the session to a different scene.</summary>
    MoveScene,

    /// <summary>Create a new NPC in the current scene or campaign.</summary>
    CreateNPC,

    /// <summary>Start a combat encounter.</summary>
    StartEncounter,

    /// <summary>Advance the combat turn to the next initiative slot.</summary>
    AdvanceTurn,

    /// <summary>Award treasure or items to one or more characters.</summary>
    AwardTreasure,

    /// <summary>Add or update a quest flag in campaign state.</summary>
    AddQuestFlag,

    /// <summary>Add or update a world flag in campaign state.</summary>
    UpdateWorldFlag,

    /// <summary>Ask the player or DM a clarifying question before proceeding.</summary>
    AskClarifyingQuestion,
}
