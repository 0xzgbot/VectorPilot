using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// SPK-0316: index-paired segment diff between old/new toolpath g-code
/// (the ghost overlay's engine helper).
/// </summary>
public class ToolpathDiffTests
{
    private static readonly string[] BaseGcode =
    {
        "G0 X0 Y0",
        "G1 X10 Y0",
        "G1 X10 Y10"
    };

    [Fact]
    public void Identical_Gcode_Has_No_OnlyInOld_Or_OnlyInNew_Segments()
    {
        var diff = ToolpathDiff.CompareLines(BaseGcode, BaseGcode);

        Assert.Equal(2, diff.Count); // two moves after the initial G0
        Assert.All(diff, d =>
        {
            Assert.False(d.OnlyInOld);
            Assert.False(d.OnlyInNew);
        });
    }

    [Fact]
    public void Extra_Line_In_New_Gcode_Marks_That_Segment_OnlyInNew()
    {
        var newGcode = new[]
        {
            "G0 X0 Y0",
            "G1 X10 Y0",
            "G1 X10 Y10",
            "G1 X0 Y10" // extra closing move
        };

        var diff = ToolpathDiff.CompareLines(BaseGcode, newGcode);

        Assert.Equal(3, diff.Count);
        Assert.All(diff.Take(2), d =>
        {
            Assert.False(d.OnlyInOld);
            Assert.False(d.OnlyInNew);
        });
        var extra = diff[2];
        Assert.True(extra.OnlyInNew);
        Assert.False(extra.OnlyInOld);
        Assert.Equal(10, extra.X0);
        Assert.Equal(10, extra.Y0);
        Assert.Equal(0, extra.X1);
        Assert.Equal(10, extra.Y1);
    }

    [Fact]
    public void Removed_Line_Marks_That_Segment_OnlyInOld()
    {
        var oldGcode = new[]
        {
            "G0 X0 Y0",
            "G1 X10 Y0",
            "G1 X10 Y10",
            "G1 X0 Y10" // this closing move is gone in the new program
        };

        var diff = ToolpathDiff.CompareLines(oldGcode, BaseGcode);

        Assert.Equal(3, diff.Count);
        Assert.All(diff.Take(2), d =>
        {
            Assert.False(d.OnlyInOld);
            Assert.False(d.OnlyInNew);
        });
        var removed = diff[2];
        Assert.True(removed.OnlyInOld);
        Assert.False(removed.OnlyInNew);
        Assert.Equal(10, removed.X0);
        Assert.Equal(10, removed.Y0);
        Assert.Equal(0, removed.X1);
        Assert.Equal(10, removed.Y1);
    }

    [Fact]
    public void CompareLines_On_Empty_Lists_Returns_Empty()
    {
        var diff = ToolpathDiff.CompareLines(Array.Empty<string>(), Array.Empty<string>());

        Assert.Empty(diff);
    }
}
