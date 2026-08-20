using System.Globalization;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Corner rounding gadget — real tangent arcs, not the Mac's placeholder.
///
/// ShopPilot's GadgetToolpaths.generateRounding returns a SINGLE segment at (0,0) with
/// the comment "Full implementation would need vector geometry analysis", and
/// previewRounding draws a dashed radius indicator. Porting either verbatim would have
/// imported a stub that reads as a shipped feature. These tests pin the geometry that
/// makes rounding real.
/// </summary>
public class RoundingGadgetTests
{
    private static readonly StrategyRegistry Reg = new();

    private static List<VectorPoint> Square(double x = 0, double y = 0, double w = 60, double h = 40) => new()
    {
        new VectorPoint(x, y),
        new VectorPoint(x + w, y),
        new VectorPoint(x + w, y + h),
        new VectorPoint(x, y + h)
    };

    private static VectorShape SquareShape()
    {
        var s = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        s.Points.AddRange(Square());
        return s;
    }

    private static RoundingGadget.Params P(double r = 6) => new()
    {
        RadiusMm = r,
        SegmentsPerCorner = 8,
        CutDepthMm = 3,
        StepDownMm = 3,
        FeedRateMmPerMin = 1000,
        SafeZHeightMm = 5
    };

    // ---- geometry ----

    [Fact]
    public void All_Four_Corners_Of_A_Square_Are_Rounded()
    {
        RoundingGadget.RoundCorners(Square(), 6, 8, 170, out int corners);
        Assert.Equal(4, corners);
    }

    [Fact]
    public void Rounding_Adds_Points_Instead_Of_Keeping_Sharp_Vertices()
    {
        var rounded = RoundingGadget.RoundCorners(Square(), 6, 8, 170, out _);

        // 4 corners × 9 arc points each.
        Assert.True(rounded.Count > 4, $"only {rounded.Count} points — corners were not arced");

        // The original sharp corner must no longer be present.
        Assert.DoesNotContain(rounded, p => Math.Abs(p.X - 0) < 1e-9 && Math.Abs(p.Y - 0) < 1e-9);
    }

    [Fact]
    public void The_Rounded_Outline_Stays_Inside_The_Original_Bounds()
    {
        var rounded = RoundingGadget.RoundCorners(Square(), 6, 8, 170, out _);

        Assert.True(rounded.Min(p => p.X) >= -1e-6, "outline escaped left edge");
        Assert.True(rounded.Min(p => p.Y) >= -1e-6, "outline escaped bottom edge");
        Assert.True(rounded.Max(p => p.X) <= 60 + 1e-6, "outline escaped right edge");
        Assert.True(rounded.Max(p => p.Y) <= 40 + 1e-6, "outline escaped top edge");
    }

    [Fact]
    public void Arc_Points_Sit_At_The_Radius_From_The_Corner_Centre()
    {
        // For a 90° corner at the origin with r=6, the arc centre is at (6,6).
        var rounded = RoundingGadget.RoundCorners(Square(), 6, 16, 170, out _);

        var nearOrigin = rounded.Where(p => p.X < 12 && p.Y < 12).ToList();
        Assert.NotEmpty(nearOrigin);

        foreach (var p in nearOrigin)
        {
            double d = Math.Sqrt((p.X - 6) * (p.X - 6) + (p.Y - 6) * (p.Y - 6));
            Assert.Equal(6.0, d, 1);
        }
    }

    [Fact]
    public void A_Larger_Radius_Removes_More_Material()
    {
        var small = RoundingGadget.RoundCorners(Square(), 2, 8, 170, out _);
        var large = RoundingGadget.RoundCorners(Square(), 15, 8, 170, out _);

        // The bigger radius pulls the outline further from the original corner.
        double dSmall = small.Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        double dLarge = large.Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));

        Assert.True(dLarge > dSmall, $"large r={dLarge:F3} should clear the corner more than small r={dSmall:F3}");
    }

    [Fact]
    public void A_Radius_Too_Large_For_The_Edge_Is_Clamped_Not_Overshot()
    {
        // 100mm radius on a 60×40 square must not invert the shape.
        var rounded = RoundingGadget.RoundCorners(Square(), 100, 8, 170, out _);

        Assert.True(rounded.Min(p => p.X) >= -1e-6);
        Assert.True(rounded.Max(p => p.X) <= 60 + 1e-6);
        Assert.True(rounded.Min(p => p.Y) >= -1e-6);
        Assert.True(rounded.Max(p => p.Y) <= 40 + 1e-6);
    }

    [Fact]
    public void A_Near_Straight_Vertex_Is_Left_Alone()
    {
        // Three nearly-collinear points: nothing to round.
        var almostStraight = new List<VectorPoint>
        {
            new VectorPoint(0, 0), new VectorPoint(50, 0.05), new VectorPoint(100, 0),
            new VectorPoint(50, -40)
        };

        RoundingGadget.RoundCorners(almostStraight, 5, 8, 170, out int corners);
        Assert.True(corners < 4, "a near-straight vertex was rounded anyway");
    }

    [Fact]
    public void Zero_Radius_Returns_The_Original_Outline()
    {
        var original = Square();
        var rounded = RoundingGadget.RoundCorners(original, 0, 8, 170, out int corners);

        Assert.Equal(0, corners);
        Assert.Equal(original.Count, rounded.Count);
    }

    [Fact]
    public void A_Degenerate_Polygon_Is_Returned_Unchanged()
    {
        var twoPoints = new List<VectorPoint> { new VectorPoint(0, 0), new VectorPoint(10, 0) };
        var rounded = RoundingGadget.RoundCorners(twoPoints, 5, 8, 170, out int corners);

        Assert.Equal(0, corners);
        Assert.Equal(2, rounded.Count);
    }

    // ---- emitted program ----

    [Fact]
    public void It_Emits_A_Cutting_Program()
    {
        var r = RoundingGadget.Compute(new[] { SquareShape() }, P());

        Assert.Null(r.Error);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 X"));
        Assert.Contains(r.GcodeLines, l => l.StartsWith("M3"));
        Assert.Contains("M5", r.GcodeLines);
        Assert.Equal(4, r.CornersRounded);
    }

    [Fact]
    public void Every_Cutting_Move_Follows_The_Rounded_Outline()
    {
        var r = RoundingGadget.Compute(new[] { SquareShape() }, P());

        foreach (var line in r.GcodeLines.Where(l => l.StartsWith("G1 X") && l.Contains('Y')))
        {
            var toks = line.Split(' ');
            double x = double.Parse(toks.First(t => t[0] == 'X')[1..], CultureInfo.InvariantCulture);
            double y = double.Parse(toks.First(t => t[0] == 'Y')[1..], CultureInfo.InvariantCulture);

            Assert.True(x >= -1e-3 && x <= 60 + 1e-3, $"X={x:F3} left the shape");
            Assert.True(y >= -1e-3 && y <= 40 + 1e-3, $"Y={y:F3} left the shape");
        }
    }

    [Fact]
    public void Deeper_Cuts_Add_More_Passes()
    {
        var shallow = P(); shallow.CutDepthMm = 3; shallow.StepDownMm = 3;
        var deep = P(); deep.CutDepthMm = 12; deep.StepDownMm = 3;

        Assert.True(RoundingGadget.Compute(new[] { SquareShape() }, deep).GcodeLines.Count >
                    RoundingGadget.Compute(new[] { SquareShape() }, shallow).GcodeLines.Count);
    }

    // ---- registered and refusing badly ----

    [Fact]
    public void It_Is_A_Registered_Reachable_Strategy()
    {
        var entry = Reg.Find("rounding");
        Assert.NotNull(entry);
        Assert.Equal("Corner Rounding", entry!.DisplayName);
        Assert.Equal(ToolpathStrategy.Rounding, StrategyKeyMap.ToStrategy("rounding"));
    }

    [Fact]
    public void It_Emits_Through_The_Registry()
    {
        var entry = Reg.Find("rounding")!;
        var result = entry.Compute(new[] { SquareShape() }, null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Null(result.Error);
    }

    [Fact]
    public void No_Shapes_Is_Refused_With_A_Reason()
    {
        var r = RoundingGadget.Compute(Array.Empty<VectorShape>(), P());

        Assert.Empty(r.GcodeLines);
        Assert.Contains("needs a closed shape", r.Error!);
    }

    [Fact]
    public void A_Zero_Radius_Is_Refused_Rather_Than_Cutting_Sharp()
    {
        var r = RoundingGadget.Compute(new[] { SquareShape() }, P(0));

        Assert.Empty(r.GcodeLines);
        Assert.Contains("radius", r.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
