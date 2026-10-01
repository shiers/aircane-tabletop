using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Applies sensible defaults to a GameSystemDefinition for any omitted optional sections.
/// When optional sections are null/empty, this fills them with freeform/generic defaults.
/// Returns a new GameSystemDefinition instance - does not mutate the original.
/// </summary>
public class GameSystemDefaultsApplicator : IGameSystemDefaultsApplicator
{
    /// <summary>
    /// Generic AI guidance applied when no system-specific guidance is provided.
    /// </summary>
    internal static readonly AiGuidance DefaultAiGuidance = new()
    {
        SystemPromptNotes = "This is a tabletop RPG. Follow the game system's rules as described in the definition.",
        ToneGuidance = "Adapt tone to the genre and setting of the game system.",
        MechanicalNotes = "Use the dice conventions and resolution rules defined in the game system definition.",
        CommonMistakes = ["Do not assume d20-based mechanics unless the dice convention specifies it."],
        RollFormatExample = null
    };

    /// <summary>
    /// Default freeform action economy applied when no action economy is defined.
    /// </summary>
    internal static readonly ActionEconomyDefinition DefaultActionEconomy = new()
    {
        Type = ActionEconomyType.Freeform
    };

    /// <inheritdoc />
    public GameSystemDefinition ApplyDefaults(GameSystemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Create a new instance with the same required fields
        var result = new GameSystemDefinition(
            identifier: definition.Identifier,
            name: definition.Name,
            version: definition.Version,
            schemaVersion: definition.SchemaVersion,
            license: definition.License,
            publisher: definition.Publisher,
            genre: definition.Genre,
            description: definition.Description);

        // Copy over all existing properties
        result.Tags = definition.Tags;
        result.DiceConventions = definition.DiceConventions;
        result.ResolutionRules = definition.ResolutionRules;
        result.CharacterSchema = definition.CharacterSchema;
        result.IsActive = definition.IsActive;
        result.IsBuiltIn = definition.IsBuiltIn;
        result.UpdatedAt = definition.UpdatedAt;

        // ConditionSet: leave as empty list when null/empty - freeform conditions are allowed
        result.ConditionSet = definition.ConditionSet;

        // ActionEconomy: apply freeform default when null
        result.ActionEconomy = definition.ActionEconomy ?? DefaultActionEconomy;

        // EncounterBudget: leave as null when null - no automated balancing
        result.EncounterBudget = definition.EncounterBudget;

        // AiGuidance: apply generic TTRPG guidance when null
        result.AiGuidance = definition.AiGuidance ?? DefaultAiGuidance;

        return result;
    }
}
