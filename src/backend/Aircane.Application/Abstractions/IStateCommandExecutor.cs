using Aircane.Application.AiRuntime;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Single entry point for AI-initiated state changes. Validates the command,
/// checks role permission and authority level, then either applies directly
/// or queues as a proposal for host approval.
/// </summary>
public interface IStateCommandExecutor
{
    /// <summary>
    /// Executes a validated state command. The command is validated, checked against
    /// the AI role's permissions and the campaign's authority level, then either
    /// applied directly to state or queued as a proposal.
    /// </summary>
    /// <returns>The result of the command execution attempt.</returns>
    Task<StateCommandResult> ExecuteAsync(
        AiProposedAction command,
        StateCommandContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context required to execute a state command, including campaign and session identifiers
/// and the current AI configuration.
/// </summary>
public sealed record StateCommandContext(
    Guid CampaignId,
    Guid SessionId,
    Domain.Enums.AiRole AiRole,
    Domain.Enums.AiAuthority AiAuthority);

/// <summary>
/// The result of a state command execution attempt.
/// </summary>
public sealed record StateCommandResult
{
    /// <summary>Whether the command was successfully processed (applied or queued).</summary>
    public required bool Success { get; init; }

    /// <summary>How the command was routed.</summary>
    public required StateCommandOutcome Outcome { get; init; }

    /// <summary>Error message when validation or permission check fails.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The proposal ID if the command was queued for approval.</summary>
    public Guid? ProposalId { get; init; }

    public static StateCommandResult Applied() => new()
    {
        Success = true,
        Outcome = StateCommandOutcome.Applied
    };

    public static StateCommandResult Queued(Guid proposalId) => new()
    {
        Success = true,
        Outcome = StateCommandOutcome.QueuedForApproval,
        ProposalId = proposalId
    };

    public static StateCommandResult ValidationFailed(string error) => new()
    {
        Success = false,
        Outcome = StateCommandOutcome.ValidationFailed,
        ErrorMessage = error
    };

    public static StateCommandResult PermissionDenied(string error) => new()
    {
        Success = false,
        Outcome = StateCommandOutcome.PermissionDenied,
        ErrorMessage = error
    };
}

/// <summary>
/// Describes how a state command was handled.
/// </summary>
public enum StateCommandOutcome
{
    /// <summary>The command was validated and applied directly to campaign state.</summary>
    Applied,

    /// <summary>The command was validated and queued for host approval.</summary>
    QueuedForApproval,

    /// <summary>The command failed validation (invalid parameters).</summary>
    ValidationFailed,

    /// <summary>The AI role does not have permission for this action.</summary>
    PermissionDenied
}
