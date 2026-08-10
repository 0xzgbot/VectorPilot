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

    // ---- Verify0209 parity cases (exact expectations from the Mac CLT) ----

    [Theory]
    [InlineData("2*3+4", 10.0)]   // precedence
    [InlineData("2 * 3 + 4", 10.0)]
    [InlineData("2440 / 2", 1220.0)]
    [InlineData("10/4", 2.5)]
    [InlineData("7-2-1", 4.0)]    // left-assoc minus
    [InlineData("1.5*2", 3.0)]    // decimals
    [InlineData("2*(3+4)", 14.0)]
    [InlineData("2*π", 2 * Math.PI)]
    public void Verify0209_Arithmetic(string expr, double expected)
    {
        Assert.Equal(expected, ExpressionCalculator.Evaluate(expr) ?? double.NaN, 9);
    }

    [Fact]
    public void Verify0209_Longest_Key_Substitution()
    {
        // "wide"→7 and "width"→100: longest key first must not clobber.
        var v = new List<DocumentVariable>
        {
            new() { Key = "wide", Value = "7" },
            new() { Key = "width", Value = "100" },
            new() { Key = "depth", Value = "4" }
        };
        Assert.Equal(7.0, ExpressionCalculator.Evaluate("wide", v) ?? double.NaN, 6);
        Assert.Equal(100.0, ExpressionCalculator.Evaluate("width", v) ?? double.NaN, 6);
        Assert.Equal(400.0, ExpressionCalculator.Evaluate("width*depth", v) ?? double.NaN, 6);
        Assert.Equal(2 * Math.PI * 3, ExpressionCalculator.Evaluate("2*pi*r", v.Concat(new[] { new DocumentVariable { Key = "r", Value = "3" } }).ToList()) ?? double.NaN, 9);
    }
}
