namespace Aircane.Application.Abstractions;

/// <summary>
/// The structured JSON output returned by the AI provider for any response that may
/// include state-changing proposed actions.
/// <para>
/// The AI proposes actions; the backend validates and applies them. The AI must never
/// directly mutate campaign state.
/// </para>
/// </summary>
public sealed record AiStructuredOutput
{
    /// <summary>
    /// Public narration text shown to all session participants (or only the DM,
    /// depending on the AI role and visibility settings).
    /// </summary>
    public required string Narration { get; init; }

    /// <summary>
    /// Optional private note visible only to the DM/host. Null when not applicable.
    /// </summary>
    public string? PrivateDmNote { get; init; }

    /// <summary>
    /// Source document chunks the AI used to ground its response.
    /// Empty when no retrieval was performed.
    /// </summary>
    public IReadOnlyList<AiRulesCitation> RulesCitations { get; init; } = [];

    /// <summary>
    /// Structured actions the AI proposes. Each action must be validated and
    /// authorized before being applied to campaign state.
    /// </summary>
    public IReadOnlyList<AiProposedAction> ProposedActions { get; init; } = [];
}

/// <summary>
/// A citation linking an AI response to a specific indexed document chunk.
/// </summary>
public sealed record AiRulesCitation
{
    /// <summary>The ID of the source document that was retrieved.</summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>The ID of the specific document chunk that was cited.</summary>
    public required Guid ChunkId { get; init; }

    /// <summary>A short human-readable summary of the cited content.</summary>
    public required string Summary { get; init; }
}

/// <summary>
/// A single action proposed by the AI. The <see cref="Type"/> discriminates which
/// fields are relevant for each action kind.
/// </summary>
public sealed record AiProposedAction
{
    /// <summary>The kind of action being proposed.</summary>
    public required AiActionType Type { get; init; }

    /// <summary>
    /// The character or creature this action targets.
    /// Null for scene-level or campaign-level actions.
    /// </summary>
    public Guid? CharacterId { get; init; }

    /// <summary>
    /// Human-readable label for the action (e.g. "Dexterity (Stealth) check").
    /// Used for display in the approval queue.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Dice formula for roll requests (e.g. "1d20+5").
    /// Null for non-roll actions.
    /// </summary>
    public string? Formula { get; init; }

    /// <summary>
    /// Difficulty class for roll requests. Null when no DC applies.
    /// </summary>
    public int? Dc { get; init; }

    /// <summary>
    /// Visibility of the action result: "public" or "private".
    /// Defaults to "public" when not specified.
    /// </summary>
    public string? Visibility { get; init; }

    /// <summary>
    /// The AI's reasoning for proposing this action. Shown in the approval queue
    /// and preserved in the audit log.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Numeric value for damage, healing, or similar quantity-based actions.
    /// Null for non-quantity actions.
    /// </summary>
    public int? Amount { get; init; }

    /// <summary>
    /// Condition name for ApplyCondition / RemoveCondition actions
    /// (e.g. "Poisoned", "Prone"). Null for other action types.
    /// </summary>
    public string? ConditionName { get; init; }

    /// <summary>
    /// Target scene ID for MoveScene actions. Null for other action types.
    /// </summary>
    public Guid? TargetSceneId { get; init; }

    /// <summary>
    /// Content ID for RevealContent actions. Null for other action types.
    /// </summary>
    public Guid? ContentId { get; init; }

    /// <summary>
    /// Flag name for AddQuestFlag / UpdateWorldFlag actions. Null for other action types.
    /// </summary>
    public string? FlagName { get; init; }

    /// <summary>
    /// Flag value for AddQuestFlag / UpdateWorldFlag actions. Null for other action types.
    /// </summary>
    public string? FlagValue { get; init; }
}
