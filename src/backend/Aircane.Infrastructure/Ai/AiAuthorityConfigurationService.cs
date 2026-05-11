using Aircane.Application.Abstractions;
using Aircane.Application.AiRuntime;
using Aircane.Domain.Enums;

namespace Aircane.Infrastructure.Ai;

/// <summary>
/// Provides static AI authority configurations that define what each authority level allows.
/// These configurations are used by the AI runtime to determine whether an action requires
/// host approval or can be auto-applied based on the campaign's AI authority setting.
/// </summary>
public sealed class AiAuthorityConfigurationService : IAiAuthorityService
{
    /// <summary>
    /// Action types classified as "safe" (non-destructive). These do not modify character state,
    /// campaign state, or encounter state. They can be auto-applied at AutoApplySafeActions level.
    /// </summary>
    private static readonly IReadOnlySet<AiActionType> SafeActions = new HashSet<AiActionType>
    {
        AiActionType.Narrate,
        AiActionType.RequestRoll,
        AiActionType.AskClarifyingQuestion,
    };

    /// <summary>
    /// Action types classified as "state-changing" (destructive). These modify character state,
    /// campaign state, or encounter state and require approval at most authority levels.
    /// </summary>
    private static readonly IReadOnlySet<AiActionType> StateChangingActions = new HashSet<AiActionType>
    {
        AiActionType.ApplyDamage,
        AiActionType.ApplyHealing,
        AiActionType.ApplyCondition,
        AiActionType.RemoveCondition,
        AiActionType.RevealContent,
        AiActionType.MoveScene,
        AiActionType.CreateNPC,
        AiActionType.StartEncounter,
        AiActionType.AdvanceTurn,
        AiActionType.AwardTreasure,
        AiActionType.AddQuestFlag,
        AiActionType.UpdateWorldFlag,
    };

    private static readonly IReadOnlyDictionary<AiAuthority, AiAuthorityConfiguration> Configurations = BuildConfigurations();

    /// <inheritdoc />
    public AiAuthorityConfiguration GetConfiguration(AiAuthority authority)
    {
        if (Configurations.TryGetValue(authority, out var config))
            return config;

        throw new ArgumentOutOfRangeException(nameof(authority), authority, $"Unknown AI authority: {authority}");
    }

    /// <inheritdoc />
    public IReadOnlyList<AiAuthorityConfiguration> GetAllConfigurations()
    {
        return Configurations.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public bool RequiresApproval(AiAuthority authority, AiActionType actionType)
    {
        if (Configurations.TryGetValue(authority, out var config))
            return config.RequiresApprovalActions.Contains(actionType);

        // Unknown authority: require approval for safety.
        return true;
    }

    /// <inheritdoc />
    public bool CanAutoApply(AiAuthority authority, AiActionType actionType)
    {
        if (Configurations.TryGetValue(authority, out var config))
            return config.AutoApplyActions.Contains(actionType);

        // Unknown authority: never auto-apply.
        return false;
    }

    /// <summary>
    /// Returns the set of action types classified as safe (non-destructive).
    /// </summary>
    public static IReadOnlySet<AiActionType> GetSafeActions() => SafeActions;

    /// <summary>
    /// Returns the set of action types classified as state-changing (destructive).
    /// </summary>
    public static IReadOnlySet<AiActionType> GetStateChangingActions() => StateChangingActions;

    private static IReadOnlyDictionary<AiAuthority, AiAuthorityConfiguration> BuildConfigurations()
    {
        // All action types for reference
        var allActions = Enum.GetValues<AiActionType>().ToHashSet();

        return new Dictionary<AiAuthority, AiAuthorityConfiguration>
        {
            [AiAuthority.SuggestOnly] = new AiAuthorityConfiguration
            {
                Authority = AiAuthority.SuggestOnly,
                DisplayName = "Suggest Only",
                Description = "All state-changing actions require human approval. "
                            + "The AI can only suggest actions; nothing is applied without explicit host consent.",
                // AskClarifyingQuestion is always auto-applied (it's not a state change).
                AutoApplyActions = new HashSet<AiActionType>
                {
                    AiActionType.AskClarifyingQuestion,
                },
                RequiresApprovalActions = new HashSet<AiActionType>
                {
                    AiActionType.Narrate,
                    AiActionType.RequestRoll,
                    AiActionType.ApplyDamage,
                    AiActionType.ApplyHealing,
                    AiActionType.ApplyCondition,
                    AiActionType.RemoveCondition,
                    AiActionType.RevealContent,
                    AiActionType.MoveScene,
                    AiActionType.CreateNPC,
                    AiActionType.StartEncounter,
                    AiActionType.AdvanceTurn,
                    AiActionType.AwardTreasure,
                    AiActionType.AddQuestFlag,
                    AiActionType.UpdateWorldFlag,
                },
            },

            [AiAuthority.AskBeforeApplying] = new AiAuthorityConfiguration
            {
                Authority = AiAuthority.AskBeforeApplying,
                DisplayName = "Ask Before Applying",
                Description = "Proposed state changes are shown in a structured approval queue. "
                            + "The host must approve, edit, or reject each proposal before it is applied.",
                // Safe actions (narration, roll requests, clarifying questions) auto-apply.
                AutoApplyActions = new HashSet<AiActionType>
                {
                    AiActionType.Narrate,
                    AiActionType.RequestRoll,
                    AiActionType.AskClarifyingQuestion,
                },
                RequiresApprovalActions = new HashSet<AiActionType>
                {
                    AiActionType.ApplyDamage,
                    AiActionType.ApplyHealing,
                    AiActionType.ApplyCondition,
                    AiActionType.RemoveCondition,
                    AiActionType.RevealContent,
                    AiActionType.MoveScene,
                    AiActionType.CreateNPC,
                    AiActionType.StartEncounter,
                    AiActionType.AdvanceTurn,
                    AiActionType.AwardTreasure,
                    AiActionType.AddQuestFlag,
                    AiActionType.UpdateWorldFlag,
                },
            },

            [AiAuthority.AutoApplySafeActions] = new AiAuthorityConfiguration
            {
                Authority = AiAuthority.AutoApplySafeActions,
                DisplayName = "Auto-Apply Safe Actions",
                Description = "Non-destructive actions (narration, public notes, roll requests) are applied automatically. "
                            + "State-changing actions still require host approval.",
                AutoApplyActions = new HashSet<AiActionType>
                {
                    AiActionType.Narrate,
                    AiActionType.RequestRoll,
                    AiActionType.AskClarifyingQuestion,
                },
                RequiresApprovalActions = new HashSet<AiActionType>
                {
                    AiActionType.ApplyDamage,
                    AiActionType.ApplyHealing,
                    AiActionType.ApplyCondition,
                    AiActionType.RemoveCondition,
                    AiActionType.RevealContent,
                    AiActionType.MoveScene,
                    AiActionType.CreateNPC,
                    AiActionType.StartEncounter,
                    AiActionType.AdvanceTurn,
                    AiActionType.AwardTreasure,
                    AiActionType.AddQuestFlag,
                    AiActionType.UpdateWorldFlag,
                },
            },

            [AiAuthority.FullSessionControl] = new AiAuthorityConfiguration
            {
                Authority = AiAuthority.FullSessionControl,
                DisplayName = "Full Session Control",
                Description = "The AI may apply all state updates freely. "
                            + "Undo and audit history are preserved for every action.",
                // All actions auto-apply at full control.
                AutoApplyActions = allActions,
                RequiresApprovalActions = new HashSet<AiActionType>(),
            },
        };
    }
}
