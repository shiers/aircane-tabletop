using Aircane.Domain.DiceExpressions;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Property-based tests for dice expression round-trip (parse-print-parse).
/// **Validates: Requirements 14.6**
///
/// Property 2: Dice expression round-trip (parse-print-parse)
/// For any valid dice expression string (across all supported notation types: NdX+M, NdX>=T, NdX!, NdF+M),
/// parsing the string into an AST, printing the AST back to canonical notation, and parsing again
/// SHALL produce an equivalent AST.
/// </summary>
public class DiceExpressionRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid StandardDiceExpression AST node.
    /// Count: 1-20, Sides: 2-100, Modifier: -20 to +20.
    /// KeepDrop amount must be >= 1 and less than Count.
    /// </summary>
    private static Gen<StandardDiceExpression> StandardDiceExpressionGen =>
        from count in Gen.Choose(1, 20)
        from sides in Gen.Choose(2, 100)
        from modifier in Gen.Choose(-20, 20)
        from hasKeepDrop in Gen.Elements(true, false)
        from keepDropType in Gen.Elements(
            KeepDropType.KeepHighest,
            KeepDropType.KeepLowest,
            KeepDropType.DropHighest,
            KeepDropType.DropLowest)
        let maxKeepDrop = count > 1 ? count - 1 : 0
        from keepDropAmount in maxKeepDrop > 0 ? Gen.Choose(1, maxKeepDrop) : Gen.Constant(0)
        let keepDrop = hasKeepDrop && maxKeepDrop > 0
            ? new KeepDropDirective { Type = keepDropType, Amount = keepDropAmount }
            : null
        select new StandardDiceExpression
        {
            Count = count,
            Sides = sides,
            Modifier = keepDrop is not null ? 0 : modifier, // Parser doesn't support both keep/drop AND modifier together? Let's check...
            KeepDrop = keepDrop
        };

    /// <summary>
    /// Generates a valid StandardDiceExpression with modifier (no keep/drop).
    /// </summary>
    private static Gen<StandardDiceExpression> StandardWithModifierGen =>
        from count in Gen.Choose(1, 20)
        from sides in Gen.Choose(2, 100)
        from modifier in Gen.Choose(-20, 20)
        select new StandardDiceExpression
        {
            Count = count,
            Sides = sides,
            Modifier = modifier,
            KeepDrop = null
        };

    /// <summary>
    /// Generates a valid StandardDiceExpression with keep/drop (optional modifier).
    /// </summary>
    private static Gen<StandardDiceExpression> StandardWithKeepDropGen =>
        from count in Gen.Choose(2, 20)
        from sides in Gen.Choose(2, 100)
        from modifier in Gen.Choose(-20, 20)
        from keepDropType in Gen.Elements(
            KeepDropType.KeepHighest,
            KeepDropType.KeepLowest,
            KeepDropType.DropHighest,
            KeepDropType.DropLowest)
        from keepDropAmount in Gen.Choose(1, count - 1)
        select new StandardDiceExpression
        {
            Count = count,
            Sides = sides,
            Modifier = modifier,
            KeepDrop = new KeepDropDirective { Type = keepDropType, Amount = keepDropAmount }
        };

    /// <summary>
    /// Generates a valid PoolDiceExpression AST node.
    /// Count: 1-20, Sides: 2-100, SuccessThreshold: 1-Sides, Comparison: ">=" or ">".
    /// </summary>
    private static Gen<PoolDiceExpression> PoolDiceExpressionGen =>
        from count in Gen.Choose(1, 20)
        from sides in Gen.Choose(2, 100)
        from threshold in Gen.Choose(1, sides)
        from comparison in Gen.Elements(">=", ">")
        select new PoolDiceExpression
        {
            Count = count,
            Sides = sides,
            SuccessThreshold = threshold,
            Comparison = comparison
        };

    /// <summary>
    /// Generates a valid ExplodingDiceExpression AST node.
    /// Count: 1-20, Sides: 2-100, ExplodeThreshold: null or 1-Sides.
    /// </summary>
    private static Gen<ExplodingDiceExpression> ExplodingDiceExpressionGen =>
        from count in Gen.Choose(1, 20)
        from sides in Gen.Choose(2, 100)
        from hasThreshold in Gen.Elements(true, false)
        from threshold in Gen.Choose(1, sides)
        select new ExplodingDiceExpression
        {
            Count = count,
            Sides = sides,
            ExplodeThreshold = hasThreshold ? threshold : null
        };

    /// <summary>
    /// Generates a valid FudgeDiceExpression AST node.
    /// Count: 1-20, Modifier: -20 to +20.
    /// </summary>
    private static Gen<FudgeDiceExpression> FudgeDiceExpressionGen =>
        from count in Gen.Choose(1, 20)
        from modifier in Gen.Choose(-20, 20)
        select new FudgeDiceExpression
        {
            Count = count,
            Modifier = modifier
        };

    /// <summary>
    /// Generates any valid DiceExpressionNode (one of the four types).
    /// </summary>
    private static Gen<DiceExpressionNode> AnyDiceExpressionGen =>
        Gen.OneOf(
            StandardWithModifierGen.Select(x => (DiceExpressionNode)x),
            StandardWithKeepDropGen.Select(x => (DiceExpressionNode)x),
            PoolDiceExpressionGen.Select(x => (DiceExpressionNode)x),
            ExplodingDiceExpressionGen.Select(x => (DiceExpressionNode)x),
            FudgeDiceExpressionGen.Select(x => (DiceExpressionNode)x));

    // ── Arbitraries ───────────────────────────────────────────────────────────

    public static Arbitrary<StandardDiceExpression> StandardDiceExpressionArbitrary =>
        Arb.From(StandardWithModifierGen);

    public static Arbitrary<PoolDiceExpression> PoolDiceExpressionArbitrary =>
        Arb.From(PoolDiceExpressionGen);

    public static Arbitrary<ExplodingDiceExpression> ExplodingDiceExpressionArbitrary =>
        Arb.From(ExplodingDiceExpressionGen);

    public static Arbitrary<FudgeDiceExpression> FudgeDiceExpressionArbitrary =>
        Arb.From(FudgeDiceExpressionGen);

    public static Arbitrary<DiceExpressionNode> DiceExpressionNodeArbitrary =>
        Arb.From(AnyDiceExpressionGen);

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: For any valid StandardDiceExpression AST node,
    /// Print → Parse → Print produces the same string.
    /// **Validates: Requirements 14.6**
    /// </summary>
    [Property(Arbitrary = [typeof(DiceExpressionRoundTripPropertyTests)], MaxTest = 200)]
    public void StandardDiceExpression_PrintParseRoundTrip_ProducesSameString(StandardDiceExpression node)
    {
        var printed1 = DiceExpressionPrinter.Print(node);
        var parseResult = DiceExpressionParser.Parse(printed1);

        Assert.True(parseResult.IsSuccess,
            $"Failed to parse printed expression '{printed1}': " +
            $"Position {parseResult.Error?.Position}, Expected: {parseResult.Error?.Expected}, Actual: {parseResult.Error?.Actual}");

        var printed2 = DiceExpressionPrinter.Print(parseResult.Expression!);

        Assert.Equal(printed1, printed2);
    }

    /// <summary>
    /// Property 2b: For any valid PoolDiceExpression AST node,
    /// Print → Parse → Print produces the same string.
    /// **Validates: Requirements 14.6**
    /// </summary>
    [Property(Arbitrary = [typeof(DiceExpressionRoundTripPropertyTests)], MaxTest = 200)]
    public void PoolDiceExpression_PrintParseRoundTrip_ProducesSameString(PoolDiceExpression node)
    {
        var printed1 = DiceExpressionPrinter.Print(node);
        var parseResult = DiceExpressionParser.Parse(printed1);

        Assert.True(parseResult.IsSuccess,
            $"Failed to parse printed expression '{printed1}': " +
            $"Position {parseResult.Error?.Position}, Expected: {parseResult.Error?.Expected}, Actual: {parseResult.Error?.Actual}");

        var printed2 = DiceExpressionPrinter.Print(parseResult.Expression!);

        Assert.Equal(printed1, printed2);
    }

    /// <summary>
    /// Property 2c: For any valid ExplodingDiceExpression AST node,
    /// Print → Parse → Print produces the same string.
    /// **Validates: Requirements 14.6**
    /// </summary>
    [Property(Arbitrary = [typeof(DiceExpressionRoundTripPropertyTests)], MaxTest = 200)]
    public void ExplodingDiceExpression_PrintParseRoundTrip_ProducesSameString(ExplodingDiceExpression node)
    {
        var printed1 = DiceExpressionPrinter.Print(node);
        var parseResult = DiceExpressionParser.Parse(printed1);

        Assert.True(parseResult.IsSuccess,
            $"Failed to parse printed expression '{printed1}': " +
            $"Position {parseResult.Error?.Position}, Expected: {parseResult.Error?.Expected}, Actual: {parseResult.Error?.Actual}");

        var printed2 = DiceExpressionPrinter.Print(parseResult.Expression!);

        Assert.Equal(printed1, printed2);
    }

    /// <summary>
    /// Property 2d: For any valid FudgeDiceExpression AST node,
    /// Print → Parse → Print produces the same string.
    /// **Validates: Requirements 14.6**
    /// </summary>
    [Property(Arbitrary = [typeof(DiceExpressionRoundTripPropertyTests)], MaxTest = 200)]
    public void FudgeDiceExpression_PrintParseRoundTrip_ProducesSameString(FudgeDiceExpression node)
    {
        var printed1 = DiceExpressionPrinter.Print(node);
        var parseResult = DiceExpressionParser.Parse(printed1);

        Assert.True(parseResult.IsSuccess,
            $"Failed to parse printed expression '{printed1}': " +
            $"Position {parseResult.Error?.Position}, Expected: {parseResult.Error?.Expected}, Actual: {parseResult.Error?.Actual}");

        var printed2 = DiceExpressionPrinter.Print(parseResult.Expression!);

        Assert.Equal(printed1, printed2);
    }

    /// <summary>
    /// Property 2e: Combined - For any valid DiceExpressionNode,
    /// parse(print(node)) produces an equivalent node.
    /// **Validates: Requirements 14.6**
    /// </summary>
    [Property(Arbitrary = [typeof(DiceExpressionRoundTripPropertyTests)], MaxTest = 500)]
    public void AnyDiceExpression_ParsePrintRoundTrip_ProducesEquivalentNode(DiceExpressionNode node)
    {
        var printed = DiceExpressionPrinter.Print(node);
        var parseResult = DiceExpressionParser.Parse(printed);

        Assert.True(parseResult.IsSuccess,
            $"Failed to parse printed expression '{printed}': " +
            $"Position {parseResult.Error?.Position}, Expected: {parseResult.Error?.Expected}, Actual: {parseResult.Error?.Actual}");

        Assert.Equal(node, parseResult.Expression);
    }
}
