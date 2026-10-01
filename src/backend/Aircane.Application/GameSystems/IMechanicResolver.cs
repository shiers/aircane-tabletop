using Aircane.Domain.DiceExpressions;
using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Resolves dice mechanics by parsing expressions and rolling dice according to game system conventions.
/// </summary>
public interface IMechanicResolver
{
    /// <summary>
    /// Parses a dice expression string in the context of a game system's dice convention.
    /// Delegates to the existing DiceExpressionParser.
    /// </summary>
    /// <param name="formula">The dice expression string to parse.</param>
    /// <param name="convention">The active dice convention for context.</param>
    /// <returns>A parse result containing either the AST or an error.</returns>
    DiceParseResult ParseExpression(string formula, DiceConvention convention);

    /// <summary>
    /// Rolls dice according to the parsed expression and convention, returning full results.
    /// </summary>
    /// <param name="expression">The parsed dice expression AST node.</param>
    /// <param name="convention">The active dice convention for threshold/tier resolution.</param>
    /// <param name="random">The random source for generating die values.</param>
    /// <returns>A RollResolution with raw results, modifiers, totals, and outcome information.</returns>
    RollResolution Roll(DiceExpressionNode expression, DiceConvention convention, IRandomSource random);

    /// <summary>
    /// Resolves an action outcome by applying the resolution rule to a roll result.
    /// </summary>
    /// <param name="roll">The roll result to evaluate.</param>
    /// <param name="rule">The resolution rule defining how to interpret the roll.</param>
    /// <param name="characterState">Optional character state for attribute lookups (e.g., DC, AC, defender_total).</param>
    /// <returns>A ResolutionOutcome with exactly one outcome classification, or an error if resolution fails.</returns>
    ResolutionOutcome Resolve(RollResolution roll, ResolutionRule rule, CharacterState? characterState = null);
}
