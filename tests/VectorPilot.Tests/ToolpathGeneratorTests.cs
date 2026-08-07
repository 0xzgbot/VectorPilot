using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathGeneratorTests
{
    [Fact]
    public void Profile_On_Rectangle_Produces_Expected_Sequence()
    {
        var rect = VectorShape.Rectangle(0, 0, 4, 2);
        var g = ToolpathGenerator.GenerateProfile(rect, cutDepth: 0.25);

        Assert.Contains(g, l => l.Contains("M3 S"));
        Assert.Contains(g, l => l.StartsWith("G1 Z-0.2500"));
        Assert.Contains(g, l => l.Contains("X4.0000") && l.Contains("Y0.0000"));
        Assert.Contains(g, l => l.Contains("X4.0000") && l.Contains("Y2.0000"));
        Assert.Contains(g, l => l.Contains("M5"));
        Assert.Contains(g, l => l.Contains("M30"));
        // plunge before cutting
        int plunge = g.FindIndex(l => l.StartsWith("G1 Z-"));
        int cut = g.FindIndex(l => l.StartsWith("G1 X") || l.StartsWith("G1 Y"));
        Assert.True(plunge >= 0 && cut > plunge);
    }

    [Fact]
    public void TestJob_Is_Streamable()
    {
        var g = ToolpathGenerator.GenerateTestJob();
        Assert.NotEmpty(g);
        Assert.All(g, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }

    [Fact]
    public void Circle_Profile_Samples_To_Polyline()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 1);
        var g = ToolpathGenerator.GenerateProfile(circle);
        // sampled arc produces many XY moves
        int xyMoves = g.Count(l => l.StartsWith("G1 X"));
        Assert.True(xyMoves >= 40, $"expected >= 40 XY moves, got {xyMoves}");
    }
}
