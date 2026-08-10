using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ExpressionCalculatorTests
{
    private static readonly List<DocumentVariable> Vars = new()
    {
        new DocumentVariable { Key = "stockWidth", Value = "600" },
        new DocumentVariable { Key = "materialThickness", Value = "18" },
        new DocumentVariable { Key = "margin", Value = "2.5" }
    };

    [Theory]
    [InlineData("2 + 3", 5.0)]
    [InlineData("10 - 4", 6.0)]
    [InlineData("6 * 7", 42.0)]
    [InlineData("21 / 3", 7.0)]
    [InlineData("(2 + 3) * 4", 20.0)]
    [InlineData("-5 + 10", 5.0)]
    [InlineData("3.175 * 2", 6.35)]
    [InlineData("2 * (3 + 1) / 2", 4.0)]
    public void Arithmetic_Expressions(string expr, double expected)
    {
        Assert.Equal(expected, ExpressionCalculator.Evaluate(expr) ?? double.NaN, 6);
    }

    [Fact]
    public void Variables_Substitute_Dollar_And_Bare()
    {
        Assert.Equal(300.0, ExpressionCalculator.Evaluate("$stockWidth / 2", Vars) ?? double.NaN, 6);
        Assert.Equal(300.0, ExpressionCalculator.Evaluate("stockWidth / 2", Vars) ?? double.NaN, 6);
        Assert.Equal(41.0, ExpressionCalculator.Evaluate("materialThickness * 2 + 5", Vars) ?? double.NaN, 6);
    }

    [Fact]
    public void Pi_Constant()
    {
        Assert.Equal(Math.PI, ExpressionCalculator.Evaluate("π", Vars) ?? double.NaN, 9);
        Assert.Equal(Math.PI * 4, ExpressionCalculator.Evaluate("pi * 4", Vars) ?? double.NaN, 9);
    }

    [Fact]
    public void Unresolved_Variable_Errors_Not_Skips()
    {
        // SPK-0209 hardening: leftover letters must fail, not silently become a number.
        Assert.Null(ExpressionCalculator.Evaluate("stockWidth / 2", new List<DocumentVariable>()));
        Assert.Null(ExpressionCalculator.Evaluate("width / 2", Vars));
    }

    [Fact]
    public void Invalid_Input_Returns_Null()
    {
        Assert.Null(ExpressionCalculator.Evaluate(""));
        Assert.Null(ExpressionCalculator.Evaluate("abc"));
        Assert.Null(ExpressionCalculator.Evaluate("2 +"));
        Assert.Null(ExpressionCalculator.Evaluate("(2 + 3"));
        Assert.Null(ExpressionCalculator.Evaluate("1 / 0"));
    }

    [Fact]
    public void DrivenDimension_Resolves()
    {
        var dim = new DrivenDimension { Key = "halfWidth", Expression = "stockWidth / 2" };
        Assert.Equal(300.0, ExpressionCalculator.Resolve(dim, Vars) ?? double.NaN, 6);
    }
}
