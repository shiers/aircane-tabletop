using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Provides static AI role configurations that define what each role is allowed to do.
/// These configurations are used by the AI runtime to gate actions based on the campaign's AI role setting.
/// </summary>
public sealed class AiRoleConfigurationService : IAiRoleConfigurationService
{
    private static readonly IReadOnlyDictionary<AiRole, AiRoleConfiguration> Configurations = BuildConfigurations();

    /// <inheritdoc />
    public AiRoleConfiguration GetConfiguration(AiRole role)
    {
        if (Configurations.TryGetValue(role, out var config))
            return config;

        throw new ArgumentOutOfRangeException(nameof(role), role, $"Unknown AI role: {role}");
    }

    /// <inheritdoc />
    public IReadOnlyList<AiRoleConfiguration> GetAllConfigurations()
    {
        return Configurations.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public bool HasCapability(AiRole role, AiRoleCapability capability)
    {
        return Configurations.TryGetValue(role, out var config) && config.Capabilities.Contains(capability);
    }

    /// <inheritdoc />
    public IReadOnlySet<AiRoleCapability> GetCapabilities(AiRole role)
    {
        if (Configurations.TryGetValue(role, out var config))
            return config.Capabilities;

        return new HashSet<AiRoleCapability>();
    }

    /// <inheritdoc />
    public bool IsActionPermitted(AiRole role, AiActionType actionType)
    {
        var requiredCapability = MapActionToCapability(actionType);
        if (requiredCapability is null)
            return true; // Actions with no capability requirement (e.g., Narrate in all modes) are always permitted.

        return HasCapability(role, requiredCapability.Value);
    }

    /// <summary>
    /// Maps an AI action type to the capability required to perform it.
    /// Returns null if the action requires no specific capability (always allowed).
    /// </summary>
    private static AiRoleCapability? MapActionToCapability(AiActionType actionType)
    {
        return actionType switch
        {
            AiActionType.Narrate => AiRoleCapability.Narration,
            AiActionType.RequestRoll => AiRoleCapability.RequestRolls,
            AiActionType.ApplyDamage => AiRoleCapability.ProposeStateChanges,
            AiActionType.ApplyHealing => AiRoleCapability.ProposeStateChanges,
            AiActionType.ApplyCondition => AiRoleCapability.ProposeStateChanges,
            AiActionType.RemoveCondition => AiRoleCapability.ProposeStateChanges,
            AiActionType.RevealContent => AiRoleCapability.RevealContent,
            AiActionType.MoveScene => AiRoleCapability.ProposeStateChanges,
            AiActionType.CreateNPC => AiRoleCapability.RunNpcs,
            AiActionType.StartEncounter => AiRoleCapability.CombatSupport,
            AiActionType.AdvanceTurn => AiRoleCapability.CombatSupport,
            AiActionType.AwardTreasure => AiRoleCapability.ProposeStateChanges,
            AiActionType.AddQuestFlag => AiRoleCapability.ProposeStateChanges,
            AiActionType.UpdateWorldFlag => AiRoleCapability.ProposeStateChanges,
            AiActionType.AskClarifyingQuestion => null, // Always allowed — no state change.
            _ => AiRoleCapability.ProposeStateChanges, // Default: require state change capability.
        };
    }

    private static IReadOnlyDictionary<AiRole, AiRoleConfiguration> BuildConfigurations()
    {
        return new Dictionary<AiRole, AiRoleConfiguration>
        {
            [AiRole.Assistant] = new AiRoleConfiguration
            {
                Role = AiRole.Assistant,
                DisplayName = "Assistant",
                Description = "Limited to suggestions, rules lookup, summaries, and DM-facing help. "
                            + "Does not interact directly with players unless explicitly approved.",
                Capabilities = new HashSet<AiRoleCapability>
                {
                    AiRoleCapability.Suggestions,
                    AiRoleCapability.RulesLookup,
                    AiRoleCapability.Summaries,
                    AiRoleCapability.DmFacingHelp,
                },
            },

            [AiRole.CoDm] = new AiRoleConfiguration
            {
                Role = AiRole.CoDm,
                DisplayName = "Co-DM",
                Description = "Configurable delegated duties alongside a human DM. "
                            + "Can act as rules referee, handle NPC dialogue, narrate, support combat, and scribe sessions.",
                Capabilities = new HashSet<AiRoleCapability>
                {
                    // Inherits all Assistant capabilities
                    AiRoleCapability.Suggestions,
                    AiRoleCapability.RulesLookup,
                    AiRoleCapability.Summaries,
                    AiRoleCapability.DmFacingHelp,
                    // Co-DM specific capabilities
                    AiRoleCapability.RulesReferee,
                    AiRoleCapability.NpcDialogue,
                    AiRoleCapability.Narration,
                    AiRoleCapability.CombatSupport,
                    AiRoleCapability.SessionScribe,
                },
            },

            [AiRole.FullDm] = new AiRoleConfiguration
            {
                Role = AiRole.FullDm,
                DisplayName = "Full DM",
                Description = "AI runs the full session as DM. Can narrate, request rolls, reveal scene content, "
                            + "run NPCs, and propose state changes according to authority settings.",
                Capabilities = new HashSet<AiRoleCapability>
                {
                    // All capabilities
                    AiRoleCapability.Suggestions,
                    AiRoleCapability.RulesLookup,
                    AiRoleCapability.Summaries,
                    AiRoleCapability.DmFacingHelp,
                    AiRoleCapability.RulesReferee,
                    AiRoleCapability.NpcDialogue,
                    AiRoleCapability.Narration,
                    AiRoleCapability.CombatSupport,
                    AiRoleCapability.SessionScribe,
                    AiRoleCapability.RequestRolls,
                    AiRoleCapability.RevealContent,
                    AiRoleCapability.RunNpcs,
                    AiRoleCapability.ProposeStateChanges,
                },
            },

            [AiRole.Hybrid] = new AiRoleConfiguration
            {
                Role = AiRole.Hybrid,
                DisplayName = "Hybrid",
                Description = "Scene-level AI role overrides are allowed. The base role can be changed per scene, "
                            + "enabling flexible delegation during play.",
                Capabilities = new HashSet<AiRoleCapability>
                {
                    // Hybrid starts with Full DM capabilities plus scene-level override
                    AiRoleCapability.Suggestions,
                    AiRoleCapability.RulesLookup,
                    AiRoleCapability.Summaries,
                    AiRoleCapability.DmFacingHelp,
                    AiRoleCapability.RulesReferee,
                    AiRoleCapability.NpcDialogue,
                    AiRoleCapability.Narration,
                    AiRoleCapability.CombatSupport,
                    AiRoleCapability.SessionScribe,
                    AiRoleCapability.RequestRolls,
                    AiRoleCapability.RevealContent,
                    AiRoleCapability.RunNpcs,
                    AiRoleCapability.ProposeStateChanges,
                    AiRoleCapability.SceneLevelOverride,
                },
            },
        };
    }
}
