namespace Aircane.Application.GameSystems;

/// <summary>
/// A simple formula evaluator that supports basic arithmetic (+, -, *, /),
/// floor(), ceil(), parentheses, numeric literals, and field references.
/// </summary>
public static class FormulaEvaluator
{
    /// <summary>
    /// Evaluates a formula expression against a set of field values.
    /// </summary>
    /// <param name="formula">The formula string (e.g., "floor((str - 10) / 2)").</param>
    /// <param name="fieldValues">A dictionary of field names to their numeric values.</param>
    /// <returns>The computed result, or null if evaluation fails (e.g., missing field reference).</returns>
    public static double? Evaluate(string formula, IReadOnlyDictionary<string, double> fieldValues)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return null;

        try
        {
            var tokens = Tokenize(formula);
            var pos = 0;
            var result = ParseExpression(tokens, ref pos, fieldValues);

            if (pos != tokens.Count)
                return null; // Unexpected tokens remaining

            return result;
        }
        catch
        {
            return null;
        }
    }

    private enum TokenType
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Multiply,
        Divide,
        LeftParen,
        RightParen,
        Comma
    }

    private record Token(TokenType Type, string Value);

    private static List<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < formula.Length)
        {
            var c = formula[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsDigit(formula[i + 1])))
            {
                var start = i;
                while (i < formula.Length && (char.IsDigit(formula[i]) || formula[i] == '.'))
                    i++;
                tokens.Add(new Token(TokenType.Number, formula[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_'))
                    i++;
                tokens.Add(new Token(TokenType.Identifier, formula[start..i]));
                continue;
            }

            switch (c)
            {
                case '+': tokens.Add(new Token(TokenType.Plus, "+")); break;
                case '-': tokens.Add(new Token(TokenType.Minus, "-")); break;
                case '*': tokens.Add(new Token(TokenType.Multiply, "*")); break;
                case '/': tokens.Add(new Token(TokenType.Divide, "/")); break;
                case '(': tokens.Add(new Token(TokenType.LeftParen, "(")); break;
                case ')': tokens.Add(new Token(TokenType.RightParen, ")")); break;
                case ',': tokens.Add(new Token(TokenType.Comma, ",")); break;
                default:
                    throw new FormulaEvaluationException($"Unexpected character '{c}' at position {i}");
            }
            i++;
        }

        return tokens;
    }

    // Expression = Term (('+' | '-') Term)*
    private static double? ParseExpression(List<Token> tokens, ref int pos, IReadOnlyDictionary<string, double> fields)
    {
        var left = ParseTerm(tokens, ref pos, fields);
        if (left is null) return null;

        while (pos < tokens.Count && (tokens[pos].Type == TokenType.Plus || tokens[pos].Type == TokenType.Minus))
        {
            var op = tokens[pos].Type;
            pos++;
            var right = ParseTerm(tokens, ref pos, fields);
            if (right is null) return null;

            left = op == TokenType.Plus ? left + right : left - right;
        }

        return left;
    }

    // Term = Unary (('*' | '/') Unary)*
    private static double? ParseTerm(List<Token> tokens, ref int pos, IReadOnlyDictionary<string, double> fields)
    {
        var left = ParseUnary(tokens, ref pos, fields);
        if (left is null) return null;

        while (pos < tokens.Count && (tokens[pos].Type == TokenType.Multiply || tokens[pos].Type == TokenType.Divide))
        {
            var op = tokens[pos].Type;
            pos++;
            var right = ParseUnary(tokens, ref pos, fields);
            if (right is null) return null;

            if (op == TokenType.Divide)
            {
                if (right == 0) return null; // Division by zero
                left = left / right;
            }
            else
            {
                left = left * right;
            }
        }

        return left;
    }

    // Unary = ('-' | '+')? Primary
    private static double? ParseUnary(List<Token> tokens, ref int pos, IReadOnlyDictionary<string, double> fields)
    {
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Minus)
        {
            pos++;
            var value = ParsePrimary(tokens, ref pos, fields);
            return value.HasValue ? -value : null;
        }

        if (pos < tokens.Count && tokens[pos].Type == TokenType.Plus)
        {
            pos++;
        }

        return ParsePrimary(tokens, ref pos, fields);
    }

    // Primary = Number | Identifier | FunctionCall | '(' Expression ')'
    private static double? ParsePrimary(List<Token> tokens, ref int pos, IReadOnlyDictionary<string, double> fields)
    {
        if (pos >= tokens.Count)
            return null;

        var token = tokens[pos];

        // Number literal
        if (token.Type == TokenType.Number)
        {
            pos++;
            return double.TryParse(token.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num) ? num : null;
        }

        // Identifier: could be a function call or a field reference
        if (token.Type == TokenType.Identifier)
        {
            var name = token.Value;
            pos++;

            // Check if it's a function call (followed by '(')
            if (pos < tokens.Count && tokens[pos].Type == TokenType.LeftParen)
            {
                return ParseFunctionCall(name, tokens, ref pos, fields);
            }

            // It's a field reference
            if (fields.TryGetValue(name, out var fieldValue))
                return fieldValue;

            // Field not found
            return null;
        }

        // Parenthesized expression
        if (token.Type == TokenType.LeftParen)
        {
            pos++; // consume '('
            var result = ParseExpression(tokens, ref pos, fields);
            if (result is null) return null;

            if (pos >= tokens.Count || tokens[pos].Type != TokenType.RightParen)
                return null; // Missing closing paren

            pos++; // consume ')'
            return result;
        }

        return null;
    }

    private static double? ParseFunctionCall(string name, List<Token> tokens, ref int pos, IReadOnlyDictionary<string, double> fields)
    {
        pos++; // consume '('

        var args = new List<double>();

        if (pos < tokens.Count && tokens[pos].Type != TokenType.RightParen)
        {
            var arg = ParseExpression(tokens, ref pos, fields);
            if (arg is null) return null;
            args.Add(arg.Value);

            while (pos < tokens.Count && tokens[pos].Type == TokenType.Comma)
            {
                pos++; // consume ','
                arg = ParseExpression(tokens, ref pos, fields);
                if (arg is null) return null;
                args.Add(arg.Value);
            }
        }

        if (pos >= tokens.Count || tokens[pos].Type != TokenType.RightParen)
            return null; // Missing closing paren

        pos++; // consume ')'

        // Evaluate built-in functions
        return name.ToLowerInvariant() switch
        {
            "floor" when args.Count == 1 => Math.Floor(args[0]),
            "ceil" when args.Count == 1 => Math.Ceiling(args[0]),
            "abs" when args.Count == 1 => Math.Abs(args[0]),
            "min" when args.Count == 2 => Math.Min(args[0], args[1]),
            "max" when args.Count == 2 => Math.Max(args[0], args[1]),
            "round" when args.Count == 1 => Math.Round(args[0], MidpointRounding.AwayFromZero),
            _ => null // Unknown function
        };
    }
}

/// <summary>
/// Exception thrown when formula evaluation encounters an error.
/// </summary>
public class FormulaEvaluationException : Exception
{
    public FormulaEvaluationException(string message) : base(message) { }
}
