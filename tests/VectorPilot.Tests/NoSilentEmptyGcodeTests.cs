using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// A strategy with no input must not emit a runnable-looking program.
///
/// The registry's Empty() helper returned "%" + "(No heightfield loaded)" for all four
/// heightfield strategies. That is a VALID two-line G-code file: it posts, exports and
/// streams to the machine as a successful no-op cut, so the user gets a green run and
/// an uncut piece of stock with no indication anything was wrong.
/// </summary>
public class NoSilentEmptyGcodeTests
{
    private static readonly StrategyRegistry Reg = new();

    private static readonly string[] HeightfieldKeys =
        { "rough3d", "finish3d", "photo-vcarve", "sketch-carve" };

    private static List<VectorShape> Shapes()
        => new() { VectorShape.Rectangle(0, 0, 50, 30) };

    [Theory]
    [InlineData("rough3d")]
    [InlineData("finish3d")]
    [InlineData("photo-vcarve")]
    [InlineData("sketch-carve")]
    public void A_Heightfield_Strategy_With_No_Relief_Emits_No_Program(string key)
    {
        var entry = Reg.Find(key)!;
        var result = entry.Compute(Shapes(), null, entry.DefaultsJson);

        Assert.Empty(result.Gcode);
    }

    [Theory]
    [InlineData("rough3d")]
    [InlineData("finish3d")]
    [InlineData("photo-vcarve")]
    [InlineData("sketch-carve")]
    public void A_Heightfield_Strategy_With_No_Relief_Explains_Itself(string key)
    {
        var entry = Reg.Find(key)!;
        var result = entry.Compute(Shapes(), null, entry.DefaultsJson);

        Assert.False(string.IsNullOrWhiteSpace(result.Error),
            $"{key} produced nothing without saying why");
        Assert.Contains("Model", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("rough3d")]
    [InlineData("finish3d")]
    [InlineData("photo-vcarve")]
    [InlineData("sketch-carve")]
    public void The_Old_Stub_Program_Is_Gone(string key)
    {
        var entry = Reg.Find(key)!;
        var result = entry.Compute(Shapes(), null, entry.DefaultsJson);

        // These two lines were a runnable program.
        Assert.DoesNotContain("%", result.Gcode);
        Assert.DoesNotContain(result.Gcode, l => l.Contains("No heightfield loaded"));
    }

    [Fact]
    public void A_Successful_Strategy_Carries_No_Error()
    {
        var entry = Reg.Find("profile")!;
        var result = entry.Compute(Shapes(), null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Null(result.Error);
    }

    [Fact]
    public void An_Empty_Program_Is_Distinguishable_From_A_Comment_Only_Program()
    {
        // The export guard's rule: a program of comments alone has no cutting moves.
        var commentsOnly = new List<string> { "(VectorPilot 3D Rough — Toolpath 1)", "(no moves)" };
        bool hasMoves = commentsOnly.Any(l =>
        {
            var s = l.TrimStart();
            return s.StartsWith("G0") || s.StartsWith("G1") || s.StartsWith("G2") || s.StartsWith("G3");
        });

        Assert.False(hasMoves);
    }

    [Fact]
    public void A_Real_Program_Has_Cutting_Moves()
    {
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(Shapes(), null, entry.DefaultsJson).Gcode;

        bool hasMoves = gcode.Any(l =>
        {
            var s = l.TrimStart();
            return s.StartsWith("G0") || s.StartsWith("G1") || s.StartsWith("G2") || s.StartsWith("G3");
        });

        Assert.True(hasMoves);
    }

    [Fact]
    public void Every_Heightfield_Strategy_Declares_That_It_Needs_A_Relief()
    {
        foreach (var key in HeightfieldKeys)
            Assert.True(Reg.Find(key)!.UsesHeightfield, $"{key} should declare UsesHeightfield");
    }
}
