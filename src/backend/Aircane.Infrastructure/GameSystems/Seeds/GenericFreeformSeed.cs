using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Infrastructure.GameSystems.Seeds;

/// <summary>
/// Produces the built-in Generic Freeform Game System Definition.
/// Used as a fallback for unrecognized game systems during migration.
/// Provides minimal structure with freeform defaults for all sections.
/// </summary>
public static class GenericFreeformSeed
{
    /// <summary>
    /// Well-known ID for the built-in Generic Freeform definition.
    /// Using a deterministic GUID so migrations can reference it reliably.
    /// </summary>
    public static readonly Guid DefinitionId = new("10000000-0000-0000-0000-000000000002");

    /// <summary>
    /// Creates the Generic Freeform Game System Definition.
    /// </summary>
    public static GameSystemDefinition Create()
    {
        var definition = new GameSystemDefinition(
            identifier: "generic-freeform",
            name: "Generic Freeform",
            version: "1.0.0",
            schemaVersion: 1,
            license: "built-in",
            publisher: null,
            genre: null,
            description: "A minimal freeform game system with no enforced mechanics. Use this for narrative games, homebrew systems without formal rules, or as a starting point for custom definitions.")
        {
            IsBuiltIn = true,
            IsActive = true,
            Tags = new List<string> { "freeform", "narrative", "generic" },
            DiceConventions = Array.Empty<DiceConvention>(),
            ResolutionRules = Array.Empty<ResolutionRule>(),
            CharacterSchema = null,
            ConditionSet = Array.Empty<ConditionDefinition>(),
            ActionEconomy = new ActionEconomyDefinition
            {
                Type = ActionEconomyType.Freeform
            },
            EncounterBudget = null,
            AiGuidance = new AiGuidance
            {
                SystemPromptNotes = "This campaign uses a freeform game system with no enforced mechanical rules. The host and players determine outcomes narratively. You may suggest dice rolls when appropriate, but there are no fixed resolution mechanics.",
                ToneGuidance = "Adapt tone to the campaign's genre and setting as described by the host.",
                MechanicalNotes = "No fixed dice conventions or resolution rules. Ask the host for clarification on any mechanical situations. Support any dice notation the players use.",
                CommonMistakes = new List<string>
                {
                    "Do not assume d20-based mechanics - this is a freeform system.",
                    "Do not enforce specific action economy rules - let the host manage turn structure.",
                    "Do not reject condition names - any freeform text condition is valid."
                },
                RollFormatExample = null
            }
        };

        SetId(definition, DefinitionId);

        return definition;
    }

    private static void SetId(GameSystemDefinition definition, Guid id)
    {
        var prop = typeof(GameSystemDefinition).BaseType!.GetProperty("Id")!;
        prop.SetValue(definition, id);
    }
}
