using Aircane.Application.DTOs.Ai;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Orchestrates the full AI DM loop for a player action:
/// load state → build RAG context → prompt AI → parse output → execute commands → return narration.
/// </summary>
public interface IPlayerActionService
{
    /// <summary>
    /// Processes a player action through the AI DM pipeline and returns
    /// narration plus the status of any proposed state-changing actions.
    /// </summary>
    Task<PlayerActionResponse> ProcessActionAsync(
        PlayerActionRequest request,
        CancellationToken cancellationToken = default);
}
