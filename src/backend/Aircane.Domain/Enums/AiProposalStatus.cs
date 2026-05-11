namespace Aircane.Domain.Enums;

/// <summary>
/// Status of an AI action proposal in the approval queue.
/// </summary>
public enum AiProposalStatus
{
    /// <summary>Awaiting host review.</summary>
    Pending,

    /// <summary>Approved by the host but not yet applied to state.</summary>
    Approved,

    /// <summary>Rejected by the host.</summary>
    Rejected,

    /// <summary>Approved and successfully applied to campaign state.</summary>
    Applied
}
