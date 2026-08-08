using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class MouldingToolpathTests
{
    private static List<VectorPoint> Rail(double y)
        => new() { new VectorPoint(0, y), new VectorPoint(30, y) };

    [Fact]
    public void Moulding_Produces_Gcode_And_Relief()
    {
        var p = new MouldingToolpathParams
        {
            Rail1 = Rail(0),
            Rail2 = Rail(8),
            Profile = SweepProfile.Rectangle,
            HeightMm = 6,
            FeedRateMmPerMin = 1000,
            SpindleRpm = 12000
        };
        var r = MouldingToolpathEngine.Compute(p);
        Assert.True(r.Success);
        Assert.NotNull(r.Relief);
        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("O=MOULDING_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l == "M3 S12000");
        Assert.Contains(r.GcodeLines, l => l == "M30");
        Assert.True(r.PassCount > 0);
    }

    [Fact]
    public void Moulding_Domes_With_Circle_Profile()
    {
        var p = new MouldingToolpathParams
        {
            Rail1 = Rail(0),
            Rail2 = Rail(8),
            Profile = SweepProfile.Circle,
            HeightMm = 6
        };
        var r = MouldingToolpathEngine.Compute(p);
        Assert.True(r.Success);
        // The relief has a curved top: surface-following G-code Z varies.
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 X") && l.Contains(" Z"));
    }

    [Fact]
    public void Moulding_Requires_Two_Rails()
    {
        var p = new MouldingToolpathParams { Rail1 = Rail(0), Rail2 = new List<VectorPoint>() };
        var r = MouldingToolpathEngine.Compute(p);
        Assert.False(r.Success);
        Assert.Contains("two rails", r.ErrorMessage);
    }
}
