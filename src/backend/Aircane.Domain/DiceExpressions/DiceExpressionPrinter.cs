namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Formats <see cref="DiceExpressionNode"/> AST nodes back to canonical dice notation strings.
/// </summary>
public static class DiceExpressionPrinter
{
    /// <summary>
    /// Prints a dice expression AST node to its canonical string notation.
    /// </summary>
    /// <param name="node">The AST node to format.</param>
    /// <returns>The canonical string representation of the dice expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the node type is not recognized.</exception>
    public static string Print(DiceExpressionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            StandardDiceExpression standard => PrintStandard(standard),
            PoolDiceExpression pool => PrintPool(pool),
            ExplodingDiceExpression exploding => PrintExploding(exploding),
            FudgeDiceExpression fudge => PrintFudge(fudge),
            _ => throw new ArgumentException($"Unsupported dice expression node type: {node.GetType().Name}", nameof(node))
        };
    }

    private static string PrintStandard(StandardDiceExpression expr)
    {
        var result = $"{expr.Count}d{expr.Sides}";

        if (expr.KeepDrop is not null)
        {
            var directive = expr.KeepDrop.Type switch
            {
                KeepDropType.KeepHighest => "kh",
                KeepDropType.KeepLowest => "kl",
                KeepDropType.DropHighest => "dh",
                KeepDropType.DropLowest => "dl",
                _ => throw new ArgumentException($"Unsupported keep/drop type: {expr.KeepDrop.Type}")
            };
            result += $"{directive}{expr.KeepDrop.Amount}";
        }

        if (expr.Modifier > 0)
            result += $"+{expr.Modifier}";
        else if (expr.Modifier < 0)
            result += $"{expr.Modifier}";

        return result;
    }

    private static string PrintPool(PoolDiceExpression expr)
    {
        return $"{expr.Count}d{expr.Sides}{expr.Comparison}{expr.SuccessThreshold}";
    }

    private static string PrintExploding(ExplodingDiceExpression expr)
    {
        var result = $"{expr.Count}d{expr.Sides}!";

        if (expr.ExplodeThreshold.HasValue)
            result += $">{expr.ExplodeThreshold.Value}";

        return result;
    }

    private static string PrintFudge(FudgeDiceExpression expr)
    {
        var result = $"{expr.Count}dF";

        if (expr.Modifier > 0)
            result += $"+{expr.Modifier}";
        else if (expr.Modifier < 0)
            result += $"{expr.Modifier}";

        return result;
    }
}
