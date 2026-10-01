using Aircane.Application.GameSystems;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

public class FormulaEvaluatorTests
{
    [Fact]
    public void Evaluate_SimpleAddition_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 5, ["b"] = 3 };
        var result = FormulaEvaluator.Evaluate("a + b", fields);
        Assert.Equal(8.0, result);
    }

    [Fact]
    public void Evaluate_SimpleSubtraction_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 10, ["b"] = 3 };
        var result = FormulaEvaluator.Evaluate("a - b", fields);
        Assert.Equal(7.0, result);
    }

    [Fact]
    public void Evaluate_Multiplication_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 4, ["b"] = 3 };
        var result = FormulaEvaluator.Evaluate("a * b", fields);
        Assert.Equal(12.0, result);
    }

    [Fact]
    public void Evaluate_Division_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 10, ["b"] = 4 };
        var result = FormulaEvaluator.Evaluate("a / b", fields);
        Assert.Equal(2.5, result);
    }

    [Fact]
    public void Evaluate_DivisionByZero_ReturnsNull()
    {
        var fields = new Dictionary<string, double> { ["a"] = 10, ["b"] = 0 };
        var result = FormulaEvaluator.Evaluate("a / b", fields);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Floor_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["str"] = 15 };
        var result = FormulaEvaluator.Evaluate("floor((str - 10) / 2)", fields);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Evaluate_Ceil_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["x"] = 2.3 };
        var result = FormulaEvaluator.Evaluate("ceil(x)", fields);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Evaluate_NestedParentheses_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["str"] = 14 };
        var result = FormulaEvaluator.Evaluate("floor((str - 10) / 2)", fields);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Evaluate_NumericLiteral_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double>();
        var result = FormulaEvaluator.Evaluate("10 + 5", fields);
        Assert.Equal(15.0, result);
    }

    [Fact]
    public void Evaluate_NegativeResult_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["str"] = 8 };
        var result = FormulaEvaluator.Evaluate("floor((str - 10) / 2)", fields);
        Assert.Equal(-1.0, result);
    }

    [Fact]
    public void Evaluate_MissingField_ReturnsNull()
    {
        var fields = new Dictionary<string, double>();
        var result = FormulaEvaluator.Evaluate("missing_field + 5", fields);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_EmptyFormula_ReturnsNull()
    {
        var fields = new Dictionary<string, double>();
        var result = FormulaEvaluator.Evaluate("", fields);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_NullFormula_ReturnsNull()
    {
        var fields = new Dictionary<string, double>();
        var result = FormulaEvaluator.Evaluate(null!, fields);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_OperatorPrecedence_MultiplyBeforeAdd()
    {
        var fields = new Dictionary<string, double> { ["a"] = 2, ["b"] = 3, ["c"] = 4 };
        var result = FormulaEvaluator.Evaluate("a + b * c", fields);
        Assert.Equal(14.0, result); // 2 + (3*4) = 14
    }

    [Fact]
    public void Evaluate_ParenthesesOverridePrecedence()
    {
        var fields = new Dictionary<string, double> { ["a"] = 2, ["b"] = 3, ["c"] = 4 };
        var result = FormulaEvaluator.Evaluate("(a + b) * c", fields);
        Assert.Equal(20.0, result); // (2+3)*4 = 20
    }

    [Fact]
    public void Evaluate_UnaryMinus_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["x"] = 5 };
        var result = FormulaEvaluator.Evaluate("-x", fields);
        Assert.Equal(-5.0, result);
    }

    [Fact]
    public void Evaluate_ComplexDndFormula_ReturnsCorrectResult()
    {
        // Proficiency bonus formula: floor((level - 1) / 4) + 2
        var fields = new Dictionary<string, double> { ["level"] = 5 };
        var result = FormulaEvaluator.Evaluate("floor((level - 1) / 4) + 2", fields);
        Assert.Equal(3.0, result); // floor(4/4) + 2 = 1 + 2 = 3
    }

    [Fact]
    public void Evaluate_MinFunction_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 5, ["b"] = 3 };
        var result = FormulaEvaluator.Evaluate("min(a, b)", fields);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Evaluate_MaxFunction_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["a"] = 5, ["b"] = 3 };
        var result = FormulaEvaluator.Evaluate("max(a, b)", fields);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Evaluate_AbsFunction_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["x"] = -7 };
        var result = FormulaEvaluator.Evaluate("abs(x)", fields);
        Assert.Equal(7.0, result);
    }

    [Fact]
    public void Evaluate_FieldWithUnderscore_ReturnsCorrectResult()
    {
        var fields = new Dictionary<string, double> { ["ability_score"] = 18 };
        var result = FormulaEvaluator.Evaluate("floor((ability_score - 10) / 2)", fields);
        Assert.Equal(4.0, result);
    }
}
