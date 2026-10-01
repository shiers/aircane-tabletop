namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Result of parsing a dice expression. Contains either a successful AST or an error.
/// </summary>
public record DiceParseResult
{
    /// <summary>The parsed expression AST node, or null if parsing failed.</summary>
    public DiceExpressionNode? Expression { get; init; }

    /// <summary>The parse error, or null if parsing succeeded.</summary>
    public DiceParseError? Error { get; init; }

    /// <summary>Whether parsing was successful.</summary>
    public bool IsSuccess => Expression is not null;

    /// <summary>Creates a successful parse result.</summary>
    public static DiceParseResult Success(DiceExpressionNode expression) =>
        new() { Expression = expression };

    /// <summary>Creates a failed parse result.</summary>
    public static DiceParseResult Failure(DiceParseError error) =>
        new() { Error = error };
}

/// <summary>
/// Describes a parse error with position and context information.
/// </summary>
public record DiceParseError
{
    /// <summary>The character position (0-based) where the error occurred.</summary>
    public int Position { get; init; }

    /// <summary>Description of what was expected at this position.</summary>
    public string Expected { get; init; } = string.Empty;

    /// <summary>What was actually found at this position.</summary>
    public string Actual { get; init; } = string.Empty;
}
