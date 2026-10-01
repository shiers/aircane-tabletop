namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Parses extended dice expression strings into a structured AST.
/// Supports standard (NdX+M), pool (NdX>=T), exploding (NdX!), and Fudge (NdF+M) notation.
/// Returns a result type rather than throwing exceptions.
/// </summary>
public static class DiceExpressionParser
{
    /// <summary>
    /// Parses a dice expression string into a <see cref="DiceParseResult"/>.
    /// </summary>
    /// <param name="expression">The dice expression to parse.</param>
    /// <returns>A result containing either the parsed AST or a parse error.</returns>
    public static DiceParseResult Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = 0,
                Expected = "dice expression",
                Actual = expression is null ? "null" : expression.Length == 0 ? "empty string" : "whitespace"
            });
        }

        var input = expression.Trim();
        var pos = 0;

        return ParseExpression(input, ref pos);
    }

    private static DiceParseResult ParseExpression(string input, ref int pos)
    {
        // Try to parse the dice count (optional, defaults to 1)
        var count = TryParseNumber(input, ref pos);

        // Expect 'd' or 'D'
        if (pos >= input.Length)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "dice count or 'd'",
                Actual = "end of input"
            });
        }

        if (input[pos] != 'd' && input[pos] != 'D')
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "dice count or 'd'",
                Actual = $"'{input[pos]}'"
            });
        }

        pos++; // consume 'd'

        var diceCount = count ?? 1;

        if (diceCount < 1)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = 0,
                Expected = "dice count >= 1",
                Actual = $"{diceCount}"
            });
        }

        // Check for Fudge dice: dF
        if (pos < input.Length && (input[pos] == 'F' || input[pos] == 'f'))
        {
            pos++; // consume 'F'
            return ParseFudge(input, ref pos, diceCount);
        }

        // Parse die sides
        var sidesStart = pos;
        var sides = TryParseNumber(input, ref pos);
        if (sides is null)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = sidesStart,
                Expected = "die sides (number) or 'F' for Fudge dice",
                Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
            });
        }

        if (sides.Value < 1)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = sidesStart,
                Expected = "die sides >= 1",
                Actual = $"{sides.Value}"
            });
        }

        // Now determine what follows: pool (>=, >), exploding (!), keep/drop (kh, kl, dh, dl), modifier (+/-), or end
        if (pos >= input.Length)
        {
            // Simple NdX
            return DiceParseResult.Success(new StandardDiceExpression
            {
                Count = diceCount,
                Sides = sides.Value,
                Modifier = 0
            });
        }

        var nextChar = input[pos];

        // Pool notation: >= or >
        if (nextChar == '>')
        {
            return ParsePool(input, ref pos, diceCount, sides.Value);
        }

        // Exploding notation: !
        if (nextChar == '!')
        {
            return ParseExploding(input, ref pos, diceCount, sides.Value);
        }

        // Keep/drop or modifier
        return ParseStandardSuffix(input, ref pos, diceCount, sides.Value);
    }

    private static DiceParseResult ParseFudge(string input, ref int pos, int diceCount)
    {
        var modifier = 0;

        if (pos < input.Length)
        {
            if (input[pos] == '+' || input[pos] == '-')
            {
                var sign = input[pos] == '+' ? 1 : -1;
                pos++; // consume sign

                var modStart = pos;
                var modValue = TryParseNumber(input, ref pos);
                if (modValue is null)
                {
                    return DiceParseResult.Failure(new DiceParseError
                    {
                        Position = modStart,
                        Expected = "modifier value (number)",
                        Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
                    });
                }

                modifier = sign * modValue.Value;
            }
            else
            {
                return DiceParseResult.Failure(new DiceParseError
                {
                    Position = pos,
                    Expected = "'+', '-', or end of expression",
                    Actual = $"'{input[pos]}'"
                });
            }
        }

        if (pos < input.Length)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "end of expression",
                Actual = $"'{input[pos]}'"
            });
        }

        return DiceParseResult.Success(new FudgeDiceExpression
        {
            Count = diceCount,
            Modifier = modifier
        });
    }

    private static DiceParseResult ParsePool(string input, ref int pos, int diceCount, int sides)
    {
        // pos is at '>'
        pos++; // consume '>'

        string comparison;
        if (pos < input.Length && input[pos] == '=')
        {
            pos++; // consume '='
            comparison = ">=";
        }
        else
        {
            comparison = ">";
        }

        var thresholdStart = pos;
        var threshold = TryParseNumber(input, ref pos);
        if (threshold is null)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = thresholdStart,
                Expected = "success threshold (number)",
                Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
            });
        }

        if (pos < input.Length)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "end of expression",
                Actual = $"'{input[pos]}'"
            });
        }

        return DiceParseResult.Success(new PoolDiceExpression
        {
            Count = diceCount,
            Sides = sides,
            SuccessThreshold = threshold.Value,
            Comparison = comparison
        });
    }

    private static DiceParseResult ParseExploding(string input, ref int pos, int diceCount, int sides)
    {
        // pos is at '!'
        pos++; // consume '!'

        int? explodeThreshold = null;

        if (pos < input.Length && input[pos] == '>')
        {
            pos++; // consume '>'

            var thresholdStart = pos;
            var threshold = TryParseNumber(input, ref pos);
            if (threshold is null)
            {
                return DiceParseResult.Failure(new DiceParseError
                {
                    Position = thresholdStart,
                    Expected = "explode threshold (number)",
                    Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
                });
            }

            explodeThreshold = threshold.Value;
        }

        if (pos < input.Length)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "end of expression or '>threshold'",
                Actual = $"'{input[pos]}'"
            });
        }

        return DiceParseResult.Success(new ExplodingDiceExpression
        {
            Count = diceCount,
            Sides = sides,
            ExplodeThreshold = explodeThreshold
        });
    }

    private static DiceParseResult ParseStandardSuffix(string input, ref int pos, int diceCount, int sides)
    {
        KeepDropDirective? keepDrop = null;
        var modifier = 0;

        // Check for keep/drop directives: kh, kl, dh, dl
        if (pos < input.Length)
        {
            var keepDropResult = TryParseKeepDrop(input, ref pos);
            if (keepDropResult.HasError)
            {
                return DiceParseResult.Failure(keepDropResult.Error!);
            }
            keepDrop = keepDropResult.Directive;
        }

        // Check for modifier: +N or -N
        if (pos < input.Length && (input[pos] == '+' || input[pos] == '-'))
        {
            var sign = input[pos] == '+' ? 1 : -1;
            pos++; // consume sign

            var modStart = pos;
            var modValue = TryParseNumber(input, ref pos);
            if (modValue is null)
            {
                return DiceParseResult.Failure(new DiceParseError
                {
                    Position = modStart,
                    Expected = "modifier value (number)",
                    Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
                });
            }

            modifier = sign * modValue.Value;
        }

        if (pos < input.Length)
        {
            return DiceParseResult.Failure(new DiceParseError
            {
                Position = pos,
                Expected = "end of expression, modifier (+/-), or keep/drop directive",
                Actual = $"'{input[pos]}'"
            });
        }

        // Validate keep/drop amount against dice count
        if (keepDrop is not null)
        {
            if (keepDrop.Amount < 1 || keepDrop.Amount >= diceCount)
            {
                return DiceParseResult.Failure(new DiceParseError
                {
                    Position = 0,
                    Expected = $"keep/drop amount between 1 and {diceCount - 1}",
                    Actual = $"{keepDrop.Amount}"
                });
            }
        }

        return DiceParseResult.Success(new StandardDiceExpression
        {
            Count = diceCount,
            Sides = sides,
            Modifier = modifier,
            KeepDrop = keepDrop
        });
    }

    private static (KeepDropDirective? Directive, bool HasError, DiceParseError? Error) TryParseKeepDrop(
        string input, ref int pos)
    {
        if (pos >= input.Length)
            return (null, false, null);

        var c = char.ToLowerInvariant(input[pos]);

        // Keep directives: kh, kl
        if (c == 'k')
        {
            pos++; // consume 'k'
            if (pos >= input.Length)
            {
                return (null, true, new DiceParseError
                {
                    Position = pos,
                    Expected = "'h' (highest) or 'l' (lowest) after 'k'",
                    Actual = "end of input"
                });
            }

            var typeChar = char.ToLowerInvariant(input[pos]);
            KeepDropType type;
            if (typeChar == 'h')
                type = KeepDropType.KeepHighest;
            else if (typeChar == 'l')
                type = KeepDropType.KeepLowest;
            else
            {
                return (null, true, new DiceParseError
                {
                    Position = pos,
                    Expected = "'h' (highest) or 'l' (lowest) after 'k'",
                    Actual = $"'{input[pos]}'"
                });
            }

            pos++; // consume 'h' or 'l'

            var amountStart = pos;
            var amount = TryParseNumber(input, ref pos);
            if (amount is null)
            {
                return (null, true, new DiceParseError
                {
                    Position = amountStart,
                    Expected = "keep amount (number)",
                    Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
                });
            }

            return (new KeepDropDirective { Type = type, Amount = amount.Value }, false, null);
        }

        // Drop directives: dh, dl
        if (c == 'd' && pos + 1 < input.Length)
        {
            var nextChar = char.ToLowerInvariant(input[pos + 1]);
            if (nextChar == 'h' || nextChar == 'l')
            {
                pos++; // consume 'd'
                KeepDropType type = nextChar == 'h' ? KeepDropType.DropHighest : KeepDropType.DropLowest;
                pos++; // consume 'h' or 'l'

                var amountStart = pos;
                var amount = TryParseNumber(input, ref pos);
                if (amount is null)
                {
                    return (null, true, new DiceParseError
                    {
                        Position = amountStart,
                        Expected = "drop amount (number)",
                        Actual = pos < input.Length ? $"'{input[pos]}'" : "end of input"
                    });
                }

                return (new KeepDropDirective { Type = type, Amount = amount.Value }, false, null);
            }
        }

        // Not a keep/drop directive - that's fine, might be a modifier or something else
        return (null, false, null);
    }

    private static int? TryParseNumber(string input, ref int pos)
    {
        var start = pos;
        while (pos < input.Length && char.IsDigit(input[pos]))
        {
            pos++;
        }

        if (pos == start)
            return null;

        return int.Parse(input[start..pos]);
    }
}
