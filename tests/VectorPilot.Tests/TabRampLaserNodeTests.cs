using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class TabRampTests
{
    private static readonly List<VectorPoint> Square = new()
    {
        new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)
    };

    private static List<string> CutLines()
    {
        var lines = new List<string> { "G1 Z-4.000 F300" };
        for (int i = 1; i < Square.Count; i++)
        {
            lines.Add($"G1 X{Square[i].X:0.000} Y{Square[i].Y:0.000} F1000");
        }
        return lines;
    }

    [Fact]
    public void Tabs_Insert_Lift_And_Return()
    {
        var lines = CutLines();
        var tabbed = TabGenerator.AddTabs(Square, lines, tabLengthMm: 2, tabSpacingMm: 10, safeZ: 5);
        Assert.True(tabbed.Count > lines.Count);
        Assert.Contains(tabbed, l => l.EndsWith("; tab"));
        Assert.Contains(tabbed, l => l.EndsWith("; tab end"));
        // 40mm perimeter, 10mm spacing → 4 tabs, each lifted and returned.
        Assert.Equal(4, tabbed.Count(l => l.EndsWith("; tab")));
        Assert.Equal(4, tabbed.Count(l => l.EndsWith("; tab end")));
    }

    [Fact]
    public void Tabs_Noop_On_Tiny_Spacing()
    {
        var lines = CutLines();
        var tabbed = TabGenerator.AddTabs(Square, lines, tabLengthMm: 0, tabSpacingMm: 10, safeZ: 5);
        Assert.Equal(lines, tabbed);
    }

    [Fact]
    public void Ramp_Smooth_Descends_Along_Path()
    {
        var ramp = RampGenerator.BuildRamp(RampGenerator.RampType.Smooth,
            new VectorPoint(0, 0), new VectorPoint(10, 0), fromZ: 0, toZ: -4, rampDistanceMm: 5, feed: 1000, plungeFeed: 300);
        Assert.True(ramp.Count > 3);
        Assert.True(ramp[0].Contains("Z-")); // already descending at step 1
        Assert.True(ramp[^1].Contains("Z-4.000")); // full depth at the end
        Assert.All(ramp, l => l.StartsWith("G1 X"));
    }

    [Fact]
    public void Ramp_None_Is_Plain_Plunge()
    {
        var ramp = RampGenerator.BuildRamp(RampGenerator.RampType.None,
            new VectorPoint(0, 0), new VectorPoint(10, 0), 0, -4, 5, 1000, 300);
        Assert.Single(ramp);
        Assert.Contains("Z-4.000", ramp[0]);
    }
}

public class LaserEngineTests
{
    [Fact]
    public void LaserCut_Traces_With_Power()
    {
        var r = LaserCutEngine.Compute(new[] { VectorShape.Rectangle(0, 0, 10, 10) }, new LaserCutParams { PowerPercent = 80 });
        Assert.Equal("O=LASER_CUT_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("M3 S800"));
        Assert.Contains(r.GcodeLines, l => l == "M5");
        Assert.True(r.FeatureCount >= 1);
    }

    [Fact]
    public void LaserFill_Hatches_Inside_Boundary()
    {
        var r = LaserFillEngine.Compute(new[] { VectorShape.Rectangle(0, 0, 10, 10) }, new LaserFillParams { LineSpacingMm = 4 });
        Assert.Equal("O=LASER_FILL_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount >= 2);
        // Fill lines stay inside 0..10.
        Assert.All(r.GcodeLines.Where(l => l.StartsWith("G1 X")), l => Assert.True(l.Contains("X10.000")));
    }

    [Fact]
    public void LaserPicture_Dithers_By_Brightness()
    {
        var h = new double[64];
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                h[j * 8 + i] = i < 4 ? 0 : 8; // dark left, bright right
        var hf = new HeightfieldData(8, 8, 1.0, 0, 0, h);
        var r = LaserPictureEngine.Compute(hf, new LaserPictureParams { DotSpacingMm = 1 });
        Assert.Equal("O=LASER_PICTURE_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount > 0);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("M3 S")); // power pulses
    }
}

public class NodeEditTests
{
    private static List<VectorPoint> Square() => new()
    {
        new(0, 0), new(10, 0), new(10, 10), new(0, 10)
    };

    [Fact]
    public void Insert_Point_At_Midpoint()
    {
        var pts = Square();
        Assert.True(NodeEditEngine.InsertPoint(pts, new VectorPoint(5, 0), out int idx));
        Assert.Equal(1, idx);
        Assert.Equal(new VectorPoint(5, 0), pts[1]);
        Assert.Equal(5, pts.Count);
    }

    [Fact]
    public void Delete_Point_Nearest()
    {
        var pts = Square();
        Assert.True(NodeEditEngine.DeletePoint(pts, new VectorPoint(10, 0)));
        Assert.Equal(3, pts.Count);
        Assert.False(pts.Contains(new VectorPoint(10, 0)));
    }

    [Fact]
    public void Delete_Keeps_Minimum_Two()
    {
        var pts = new List<VectorPoint> { new(0, 0), new(10, 0) };
        Assert.False(NodeEditEngine.DeletePoint(pts, new VectorPoint(0, 0)));
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void Move_Point_Repositions()
    {
        var pts = Square();
        Assert.True(NodeEditEngine.MovePoint(pts, new VectorPoint(0, 10), new VectorPoint(0, 12)));
        Assert.Contains(new VectorPoint(0, 12), pts);
    }

    [Fact]
    public void Split_Edge_Projects_On_Segment()
    {
        var pts = Square();
        // (9.5, 3) is clearly nearest the right edge (x=10) → projects there.
        Assert.True(NodeEditEngine.SplitEdge(pts, new VectorPoint(9.5, 3), out int idx));
        Assert.Equal(new VectorPoint(10, 3), pts[idx]);
    }
}
