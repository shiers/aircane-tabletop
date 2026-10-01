namespace Aircane.Domain.Enums;

/// <summary>
/// Controls how much the AI can change state without human approval.
/// </summary>
public enum AiAuthority
{
    /// <summary>All state-changing actions require human approval.</summary>
    SuggestOnly,

    /// <summary>Proposed state changes are queued for host approval before applying.</summary>
    AskBeforeApplying,

    /// <summary>Non-destructive actions (narration, notes, roll requests) apply automatically.</summary>
    AutoApplySafeActions,

    /// <summary>AI may apply state updates freely; undo and audit history are preserved.</summary>
    FullSessionControl
}
