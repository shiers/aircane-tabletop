namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Base type for all dice expression AST nodes.
/// </summary>
public abstract record DiceExpressionNode;

/// <summary>
/// Standard dice expression: NdX with optional modifier and keep/drop directive.
/// Examples: 2d6+3, 4d6kh3, 1d20-2, d20
/// </summary>
public record StandardDiceExpression : DiceExpressionNode
{
    /// <summary>Number of dice to roll (N).</summary>
    public int Count { get; init; }

    /// <summary>Number of sides per die (X).</summary>
    public int Sides { get; init; }

    /// <summary>Modifier to add/subtract (M). Positive or negative.</summary>
    public int Modifier { get; init; }

    /// <summary>Optional keep/drop directive.</summary>
    public KeepDropDirective? KeepDrop { get; init; }
}

/// <summary>
/// Pool dice expression: NdX>=T or NdX>T for success counting.
/// Examples: 6d6>=5, 10d10>7
/// </summary>
public record PoolDiceExpression : DiceExpressionNode
{
    /// <summary>Number of dice to roll.</summary>
    public int Count { get; init; }

    /// <summary>Number of sides per die.</summary>
    public int Sides { get; init; }

    /// <summary>The threshold value for counting successes.</summary>
    public int SuccessThreshold { get; init; }

    /// <summary>The comparison operator: ">=" or ">".</summary>
    public string Comparison { get; init; } = ">=";
}

/// <summary>
/// Exploding dice expression: NdX! or NdX!>T.
/// Examples: 2d6!, 3d10!>8
/// </summary>
public record ExplodingDiceExpression : DiceExpressionNode
{
    /// <summary>Number of dice to roll.</summary>
    public int Count { get; init; }

    /// <summary>Number of sides per die.</summary>
    public int Sides { get; init; }

    /// <summary>
    /// The threshold at which dice explode. Null means explode on maximum value.
    /// </summary>
    public int? ExplodeThreshold { get; init; }
}

/// <summary>
/// Fudge dice expression: NdF or NdF+M or NdF-M.
/// Each die produces -1, 0, or +1.
/// Examples: 4dF+2, 4dF
/// </summary>
public record FudgeDiceExpression : DiceExpressionNode
{
    /// <summary>Number of Fudge dice to roll.</summary>
    public int Count { get; init; }

    /// <summary>Modifier to add/subtract.</summary>
    public int Modifier { get; init; }
}
