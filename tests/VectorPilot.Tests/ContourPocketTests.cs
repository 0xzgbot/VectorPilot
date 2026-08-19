using System.Globalization;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Contour-parallel pocket clearing. The shipped pocket rasters scanlines clipped to
/// the outline — correct in extent, but not an offset pocket. A circle must be cleared
/// by concentric loops that follow the boundary, and no move may leave the outline.
///
/// The Mac's "spiralOut" strikes circular rings from the bounding-box centre, so it is
/// not the reference to copy; these tests pin the behaviour an offset pocket must have.
/// </summary>
public class ContourPocketTests
{
    private static List<VectorPoint> Circle(double cx, double cy, double r, int n = 64)
    {
        var pts = new List<VectorPoint>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new VectorPoint(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return pts;
    }

    private static List<VectorPoint> Square(double x, double y, double w, double h) => new()
    {
        new VectorPoint(x, y),
        new VectorPoint(x + w, y),
        new VectorPoint(x + w, y + h),
        new VectorPoint(x, y + h)
    };

    // ---- loop generation ----

    [Fact]
    public void A_Circle_Produces_Concentric_Loops()
    {
        var loops = ContourPocketEngine.GenerateLoops(Circle(50, 50, 25), toolDiameter: 6, stepover: 3);

        Assert.True(loops.Count >= 3, $"expected several loops, got {loops.Count}");
        // Each loop sits further in than the last.
        for (int i = 1; i < loops.Count; i++)
            Assert.True(loops[i].Inset > loops[i - 1].Inset);
    }

    [Fact]
    public void Loops_Stay_Inside_The_Boundary()
    {
        var boundary = Circle(50, 50, 25);
        var loops = ContourPocketEngine.GenerateLoops(boundary, toolDiameter: 6, stepover: 3);

        foreach (var loop in loops)
            foreach (var p in loop.Points)
                Assert.True(ContourPocketEngine.PointInPolygon(p, boundary),
                    $"loop point ({p.X:F2},{p.Y:F2}) escaped the boundary");
    }

    [Fact]
    public void A_Circular_Pocket_Stays_Within_Its_Radius()
    {
        // The bounding-box failure mode: points in the corners of the bbox but
        // outside the circle.
        var loops = ContourPocketEngine.GenerateLoops(Circle(50, 50, 25), toolDiameter: 6, stepover: 3);

        foreach (var loop in loops)
            foreach (var p in loop.Points)
            {
                double d = Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50));
                Assert.True(d <= 25.0 + 1e-6, $"point at radius {d:F3} is outside the 25mm circle");
            }
    }

    [Fact]
    public void The_First_Loop_Is_Offset_By_The_Tool_Radius()
    {
        var loops = ContourPocketEngine.GenerateLoops(Circle(50, 50, 25), toolDiameter: 6, stepover: 3);

        Assert.NotEmpty(loops);
        Assert.Equal(3.0, loops[0].Inset, 6);   // radius of a 6mm tool

        // On a circle the offset loop should sit at r - toolRadius.
        double avg = loops[0].Points.Average(p =>
            Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50)));
        Assert.Equal(22.0, avg, 0.5);
    }

    [Fact]
    public void A_Pocket_Smaller_Than_The_Tool_Produces_Nothing()
    {
        // 4mm square with a 6mm tool cannot be cleared.
        var loops = ContourPocketEngine.GenerateLoops(Square(0, 0, 4, 4), toolDiameter: 6, stepover: 3);
        Assert.Empty(loops);
    }

    [Fact]
    public void Degenerate_Input_Is_Rejected()
    {
        Assert.Empty(ContourPocketEngine.GenerateLoops(new List<VectorPoint>(), 6, 3));
        Assert.Empty(ContourPocketEngine.GenerateLoops(Square(0, 0, 50, 50), toolDiameter: 0, stepover: 3));
        Assert.Empty(ContourPocketEngine.GenerateLoops(Square(0, 0, 50, 50), toolDiameter: 6, stepover: 0));
    }

    [Fact]
    public void A_Finer_Stepover_Produces_More_Loops()
    {
        int coarse = ContourPocketEngine.GenerateLoops(Circle(50, 50, 25), 6, 6).Count;
        int fine = ContourPocketEngine.GenerateLoops(Circle(50, 50, 25), 6, 2).Count;

        Assert.True(fine > coarse, $"fine={fine} should exceed coarse={coarse}");
    }

    [Fact]
    public void Winding_Direction_Does_Not_Change_The_Result()
    {
        var ccw = Circle(50, 50, 25);
        var cw = Circle(50, 50, 25);
        cw.Reverse();

        int a = ContourPocketEngine.GenerateLoops(ccw, 6, 3).Count;
        int b = ContourPocketEngine.GenerateLoops(cw, 6, 3).Count;

        Assert.Equal(a, b);
    }

    // ---- emitted G-code ----

    [Fact]
    public void The_Slice_Emits_Closed_Rings_At_The_Cut_Depth()
    {
        var g = ContourPocketEngine.GenerateSlice(
            Circle(50, 50, 25), z: -3, toolDiameter: 6, stepover: 3,
            feedRate: 1000, plungeRate: 300, safeZ: 5);

        Assert.NotEmpty(g);
        Assert.Contains(g, l => l.Contains("Z-3.000"));
        Assert.Contains(g, l => l.StartsWith("G1 X"));
    }

    [Fact]
    public void No_Cutting_Move_Leaves_The_Outline()
    {
        var boundary = Circle(50, 50, 25);
        var g = ContourPocketEngine.GenerateSlice(
            boundary, z: -3, toolDiameter: 6, stepover: 3,
            feedRate: 1000, plungeRate: 300, safeZ: 5);

        foreach (var line in g.Where(l => l.StartsWith("G1 X")))
        {
            var toks = line.Split(' ');
            double x = double.Parse(toks.First(t => t.StartsWith("X"))[1..], CultureInfo.InvariantCulture);
            double y = double.Parse(toks.First(t => t.StartsWith("Y"))[1..], CultureInfo.InvariantCulture);

            double d = Math.Sqrt((x - 50) * (x - 50) + (y - 50) * (y - 50));
            Assert.True(d <= 25.0 + 0.01, $"cut at radius {d:F3} escaped the 25mm pocket");
        }
    }

    [Fact]
    public void A_Circle_Does_Not_Emit_Rectangle_Length_Traverses()
    {
        // The bbox-raster signature: a move spanning the full 50mm width.
        var g = ContourPocketEngine.GenerateSlice(
            Circle(50, 50, 25), z: -3, toolDiameter: 6, stepover: 3,
            feedRate: 1000, plungeRate: 300, safeZ: 5);

        var xs = g.Where(l => l.StartsWith("G1 X"))
                  .Select(l => double.Parse(l.Split(' ').First(t => t.StartsWith("X"))[1..], CultureInfo.InvariantCulture))
                  .ToList();

        Assert.NotEmpty(xs);
        // Every X must be inside the circle's span, not the bbox corners.
        Assert.True(xs.Min() >= 25.0 - 0.01, $"min X {xs.Min():F3} is outside the circle");
        Assert.True(xs.Max() <= 75.0 + 0.01, $"max X {xs.Max():F3} is outside the circle");
    }

    [Fact]
    public void A_Small_Pocket_Emits_No_Gcode_Rather_Than_A_Bad_Cut()
    {
        var g = ContourPocketEngine.GenerateSlice(
            Square(0, 0, 4, 4), z: -3, toolDiameter: 6, stepover: 3,
            feedRate: 1000, plungeRate: 300, safeZ: 5);

        Assert.Empty(g);
    }
}
