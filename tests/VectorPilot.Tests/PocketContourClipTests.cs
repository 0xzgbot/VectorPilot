using System.Globalization;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Pocket clearing must follow the OUTLINE, not the bounding box. The previous
/// implementation rastered `shape.Bounds()`, so a circle was machined as a
/// rectangle — a real cutting defect, not a cosmetic one.
/// </summary>
public class PocketContourClipTests
{
    private static (double X, double Y)? Move(string line)
    {
        if (!line.StartsWith("G1 X")) return null;
        var parts = line.Split(' ');
        double? x = null, y = null;
        foreach (var p in parts)
        {
            if (p.StartsWith("X")) x = double.Parse(p[1..], CultureInfo.InvariantCulture);
            if (p.StartsWith("Y")) y = double.Parse(p[1..], CultureInfo.InvariantCulture);
        }
        return x is { } xv && y is { } yv ? (xv, yv) : null;
    }

    private static List<(double X, double Y)> CutMoves(IEnumerable<string> g)
        => g.Select(Move).Where(m => m is not null).Select(m => m!.Value).ToList();

    [Fact]
    public void Circle_Pocket_Stays_Inside_The_Circle()
    {
        var circle = VectorShape.Circle(new VectorPoint(50, 50), 20);
        var g = PocketEngine.Generate(new[] { circle },
            cutDepth: 2, stepdown: 2, stepoverPercent: 45, feedRate: 1000, plungeRate: 300, spindleSpeed: 12000, safeZ: 5, toolDiameter: 6);

        var moves = CutMoves(g);
        Assert.NotEmpty(moves);

        // Every cut point must lie within the circle (plus a small tolerance).
        foreach (var (x, y) in moves)
        {
            double d = Math.Sqrt((x - 50) * (x - 50) + (y - 50) * (y - 50));
            Assert.True(d <= 20 + 1e-6, $"cut at ({x:F2},{y:F2}) is {d:F2}mm from centre — outside r=20");
        }
    }

    [Fact]
    public void Circle_Pocket_Does_Not_Reach_Bounding_Box_Corners()
    {
        var circle = VectorShape.Circle(new VectorPoint(50, 50), 20);
        var g = PocketEngine.Generate(new[] { circle },
            cutDepth: 2, stepdown: 2, stepoverPercent: 45, feedRate: 1000, plungeRate: 300, spindleSpeed: 12000, safeZ: 5, toolDiameter: 6);

        // The old bbox raster produced moves at x≈30 and x≈70 on the top/bottom
        // scanlines. A contour-clipped path cannot.
        foreach (var (x, y) in CutMoves(g))
        {
            bool nearTopOrBottom = y < 34 || y > 66;
            bool nearLeftOrRight = x < 34 || x > 66;
            Assert.False(nearTopOrBottom && nearLeftOrRight,
                $"cut at ({x:F2},{y:F2}) is in a bounding-box corner — bbox raster regression");
        }
    }

    [Fact]
    public void Triangle_Pocket_Narrows_Toward_The_Apex()
    {
        // Apex at the top: cut width must shrink as y increases.
        var tri = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        tri.Points.AddRange(new[]
        {
            new VectorPoint(0, 0), new VectorPoint(40, 0), new VectorPoint(20, 40)
        });

        var g = PocketEngine.Generate(new[] { tri },
            cutDepth: 1, stepdown: 1, stepoverPercent: 45, feedRate: 1000,
            plungeRate: 300, spindleSpeed: 12000, safeZ: 5, toolDiameter: 6);

        // Compare the widest cut in the bottom third against the top third.
        var moves = CutMoves(g);
        Assert.NotEmpty(moves);

        double Widest(double loY, double hiY)
        {
            var rows = moves.Where(m => m.Y >= loY && m.Y < hiY).ToList();
            return rows.Count == 0 ? 0 : rows.Max(m => m.X) - rows.Min(m => m.X);
        }

        double bottom = Widest(0, 14);
        double top = Widest(26, 40);

        Assert.True(bottom > 0, "expected cuts near the base");
        Assert.True(top < bottom,
            $"apex cuts ({top:F2}mm wide) must be narrower than base cuts ({bottom:F2}mm)");
    }

    [Fact]
    public void Rectangle_Pocket_Still_Fills_Fully()
    {
        // A rectangle IS its bounding box: clipping must not regress this case.
        var rect = VectorShape.Rectangle(10, 10, 40, 20);
        var g = PocketEngine.Generate(new[] { rect },
            cutDepth: 1, stepdown: 1, stepoverPercent: 45, feedRate: 1000, plungeRate: 300, spindleSpeed: 12000, safeZ: 5, toolDiameter: 6);

        var moves = CutMoves(g);
        Assert.NotEmpty(moves);
        Assert.True(moves.Max(m => m.X) > 45, "raster should reach the right side");
        Assert.True(moves.Min(m => m.X) < 15, "raster should reach the left side");
    }

    [Fact]
    public void Tool_Larger_Than_The_Shape_Cuts_Nothing()
    {
        var tiny = VectorShape.Circle(new VectorPoint(5, 5), 1);
        var g = PocketEngine.Generate(new[] { tiny },
            cutDepth: 1, stepdown: 1, stepoverPercent: 45, feedRate: 1000, plungeRate: 300, spindleSpeed: 12000, safeZ: 5, toolDiameter: 12);

        Assert.Empty(CutMoves(g));   // no cut moves — refuses rather than gouging
    }
}
