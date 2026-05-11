using Aircane.Application.DTOs.Dice;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Parses dice expressions, executes rolls, records manual rolls,
/// and provides the session roll log.
/// </summary>
public interface IDiceService
{
    /// <summary>
    /// Parses a dice expression string (e.g. "2d6+3") and returns the parsed components.
    /// Returns an invalid result with a parse error if the expression is malformed.
    /// </summary>
    ParsedDiceExpression ParseExpression(string formula);

    /// <summary>
    /// Rolls the dice for the given expression, persists the result, and returns the roll.
    /// </summary>
    Task<RollDto> RollAsync(
        RollRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a manually entered physical dice roll and returns the persisted roll.
    /// </summary>
    Task<RollDto> RecordManualRollAsync(
        ManualRollRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the roll log for a session, ordered by most recent first.
    /// </summary>
    Task<IReadOnlyList<RollDto>> GetRollLogAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
