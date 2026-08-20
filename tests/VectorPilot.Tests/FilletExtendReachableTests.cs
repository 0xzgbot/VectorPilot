using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Fillet and extend — the engines DesignPanel.DoFillet / DoExtend call.
///
/// ShapeFilletEngine and ShapeExtendEngine lived in VectorPilot.Geometry with no app
/// call-site at all, so a user could not round a corner or extend a path to meet another.
/// </summary>
public class FilletExtendReachableTests
{
    /// <summary>An L-shape: one 90-degree interior corner at (50,0).</summary>
    private static VectorShape LShape()
        => VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(50, 0), new(50, 50), new(60, 50), new(60, -10), new(0, -10)
        }, closed: true);

    private static VectorShape Square()
        => VectorShape.Rectangle(0, 0, 100, 60);

    private static VectorShape OpenLine(double x1, double y1, double x2, double y2)
        => VectorShape.Polyline(new List<VectorPoint> { new(x1, y1), new(x2, y2) }, closed: false);

    // ---- fillet ----

    [Fact]
    public void Filleting_An_L_Shape_Adds_Points_For_The_Arc()
    {
        var l = LShape();
        int before = l.Points.Count;

        var filleted = ShapeFilletEngine.Fillet(l, radius: 5);

        Assert.True(filleted.Points.Count > before,
            $"fillet produced {filleted.Points.Count} points from {before} — no arc was inserted");
    }

    [Fact]
    public void The_Sharp_Corner_Is_Gone()
    {
        var filleted = ShapeFilletEngine.Fillet(LShape(), radius: 8);

        // The original vertex at exactly (50,0) must no longer be present.
        Assert.DoesNotContain(filleted.Points,
            p => Math.Abs(p.X - 50) < 1e-9 && Math.Abs(p.Y - 0) < 1e-9);
    }

    [Fact]
    public void A_Larger_Radius_Cuts_Further_From_The_Corner()
    {
        double NearestTo(VectorShape s, double cx, double cy)
            => s.Points.Min(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));

        double small = NearestTo(ShapeFilletEngine.Fillet(LShape(), 2), 50, 0);
        double large = NearestTo(ShapeFilletEngine.Fillet(LShape(), 15), 50, 0);

        Assert.True(large > small,
            $"r=15 nearest approach {large:F3} not further from the corner than r=2 {small:F3}");
    }

    [Fact]
    public void Fillet_Keeps_The_Shape_Within_Its_Original_Bounds()
    {
        var l = LShape();
        double minX = l.Points.Min(p => p.X), maxX = l.Points.Max(p => p.X);
        double minY = l.Points.Min(p => p.Y), maxY = l.Points.Max(p => p.Y);

        var f = ShapeFilletEngine.Fillet(l, 6);

        Assert.True(f.Points.Min(p => p.X) >= minX - 1e-6);
        Assert.True(f.Points.Max(p => p.X) <= maxX + 1e-6);
        Assert.True(f.Points.Min(p => p.Y) >= minY - 1e-6);
        Assert.True(f.Points.Max(p => p.Y) <= maxY + 1e-6);
    }

    [Fact]
    public void Filleting_A_Rectangle_Rounds_All_Four_Corners()
    {
        var r = ShapeFilletEngine.Fillet(Square(), radius: 10);

        Assert.True(r.Points.Count > 4, $"rectangle fillet produced only {r.Points.Count} points");

        foreach (var corner in new[] { (0.0, 0.0), (100.0, 0.0), (100.0, 60.0), (0.0, 60.0) })
        {
            Assert.DoesNotContain(r.Points,
                p => Math.Abs(p.X - corner.Item1) < 1e-9 && Math.Abs(p.Y - corner.Item2) < 1e-9);
        }
    }

    [Fact]
    public void An_Oversized_Radius_Does_Not_Explode_The_Shape()
    {
        // 500mm radius on a 100x60 rectangle: must clamp, not invert or NaN.
        var r = ShapeFilletEngine.Fillet(Square(), radius: 500);

        Assert.All(r.Points, p =>
        {
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "fillet produced NaN");
            Assert.True(p.X >= -1e-6 && p.X <= 100 + 1e-6, $"X {p.X:F3} left the rectangle");
            Assert.True(p.Y >= -1e-6 && p.Y <= 60 + 1e-6, $"Y {p.Y:F3} left the rectangle");
        });
    }

    [Fact]
    public void A_Two_Point_Path_Has_No_Corner_To_Fillet()
    {
        var line = OpenLine(0, 0, 100, 0);
        var f = ShapeFilletEngine.Fillet(line, 5);

        Assert.Equal(line.Points.Count, f.Points.Count);
    }

    // ---- extend ----

    [Fact]
    public void Extending_Two_Lines_That_Miss_Makes_Them_Meet()
    {
        // Horizontal line ends at x=45; vertical line starts at x=50. They miss by 5mm.
        var horizontal = OpenLine(0, 0, 45, 0);
        var vertical = OpenLine(50, -20, 50, 20);

        double GapX(VectorShape h, VectorShape v)
            => v.Points.Min(p => p.X) - h.Points.Max(p => p.X);

        Assert.True(GapX(horizontal, vertical) > 0, "fixture precondition: they must not meet");

        var extended = ShapeExtendEngine.Extend(horizontal, distance: 10);

        Assert.True(extended.Points.Max(p => p.X) >= 50 - 1e-6,
            $"extended line still ends at X{extended.Points.Max(p => p.X):F3}, short of the vertical at X50");
    }

    [Fact]
    public void Extend_Lengthens_The_Path()
    {
        var line = OpenLine(0, 0, 100, 0);
        double before = line.Points.Max(p => p.X) - line.Points.Min(p => p.X);

        var e = ShapeExtendEngine.Extend(line, 20);
        double after = e.Points.Max(p => p.X) - e.Points.Min(p => p.X);

        Assert.True(after > before, $"extend left the span at {after:F3} (was {before:F3})");
    }

    [Fact]
    public void Extend_Follows_The_Path_Direction()
    {
        // A 45-degree line must extend diagonally, not axis-aligned.
        var diagonal = OpenLine(0, 0, 50, 50);
        var e = ShapeExtendEngine.Extend(diagonal, 10);

        double maxX = e.Points.Max(p => p.X), maxY = e.Points.Max(p => p.Y);
        Assert.Equal(maxX, maxY, 3);
        Assert.True(maxX > 50, $"diagonal did not extend past its end ({maxX:F3})");
    }

    [Fact]
    public void A_Bigger_Distance_Extends_Further()
    {
        double SpanOf(double d)
        {
            var e = ShapeExtendEngine.Extend(OpenLine(0, 0, 50, 0), d);
            return e.Points.Max(p => p.X) - e.Points.Min(p => p.X);
        }

        Assert.True(SpanOf(30) > SpanOf(5));
    }

    [Fact]
    public void Extend_Produces_No_NaN()
    {
        // Degenerate zero-length input must not divide by zero.
        var degenerate = OpenLine(10, 10, 10, 10);
        var e = ShapeExtendEngine.Extend(degenerate, 5);

        Assert.All(e.Points, p =>
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "extend produced NaN"));
    }
}
