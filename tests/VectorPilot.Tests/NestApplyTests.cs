using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Nesting applied to real geometry.
///
/// NestingEngine had ZERO VectorPilot.App call-sites: it computed placements that
/// nothing ever applied to a shape, so "nesting" did nothing to a document. These tests
/// call NestApply.Apply — the exact method DesignPanel.DoNest invokes.
/// </summary>
public class NestApplyTests
{
    private static VectorShape Rect(double x, double y, double w, double h)
        => VectorShape.Rectangle(x, y, w, h);

    private static (double MinX, double MinY, double MaxX, double MaxY) B(VectorShape s)
        => (s.Points.Min(p => p.X), s.Points.Min(p => p.Y),
            s.Points.Max(p => p.X), s.Points.Max(p => p.Y));

    private static bool Overlaps(VectorShape a, VectorShape b)
    {
        var (aminx, aminy, amaxx, amaxy) = B(a);
        var (bminx, bminy, bmaxx, bmaxy) = B(b);
        return aminx < bmaxx - 1e-6 && bminx < amaxx - 1e-6
            && aminy < bmaxy - 1e-6 && bminy < amaxy - 1e-6;
    }

    // ---- the placements actually move geometry ----

    [Fact]
    public void Two_Rectangles_Get_Non_Overlapping_Placements()
    {
        // Both start stacked at the origin — overlapping.
        var a = Rect(0, 0, 100, 60);
        var b = Rect(0, 0, 100, 60);
        Assert.True(Overlaps(a, b), "fixture precondition: they start overlapping");

        var r = NestApply.Apply(new[] { a, b }, 600, 400);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Placed);
        Assert.False(Overlaps(a, b), "nesting left the two parts overlapping");
    }

    [Fact]
    public void Placements_Stay_Inside_The_Sheet()
    {
        var shapes = Enumerable.Range(0, 6).Select(_ => Rect(0, 0, 120, 80)).ToList();

        var r = NestApply.Apply(shapes, 600, 400);
        Assert.True(r.Ok, r.Error);

        foreach (var s in shapes.Take(r.Placed))
        {
            var (minx, miny, maxx, maxy) = B(s);
            Assert.True(minx >= -1e-6, $"part crosses the left edge at {minx:F3}");
            Assert.True(miny >= -1e-6, $"part crosses the bottom edge at {miny:F3}");
            Assert.True(maxx <= 600 + 1e-6, $"part overhangs the right edge at {maxx:F3}");
            Assert.True(maxy <= 400 + 1e-6, $"part overhangs the top edge at {maxy:F3}");
        }
    }

    [Fact]
    public void Geometry_Really_Moved()
    {
        // A single part must still be repositioned to the sheet origin area, not left
        // wherever the user drew it.
        var s = Rect(450, 320, 80, 50);
        var beforeMin = B(s);

        var r = NestApply.Apply(new[] { s }, 600, 400);
        Assert.True(r.Ok, r.Error);

        var afterMin = B(s);
        Assert.True(Math.Abs(beforeMin.MinX - afterMin.MinX) > 1e-6
                 || Math.Abs(beforeMin.MinY - afterMin.MinY) > 1e-6,
            "the shape's coordinates are unchanged — the placement was never applied");
    }

    [Fact]
    public void Part_Size_Survives_Nesting()
    {
        var s = Rect(0, 0, 100, 60);
        var r = NestApply.Apply(new[] { s }, 600, 400);

        Assert.True(r.Ok, r.Error);
        var (minx, miny, maxx, maxy) = B(s);

        // Width/height may swap if the engine rotated the part 90 degrees.
        double w = maxx - minx, h = maxy - miny;
        bool sameSize = (Math.Abs(w - 100) < 0.5 && Math.Abs(h - 60) < 0.5)
                     || (Math.Abs(w - 60) < 0.5 && Math.Abs(h - 100) < 0.5);
        Assert.True(sameSize, $"part is now {w:F2}x{h:F2} — nesting distorted it");
    }

    [Fact]
    public void Utilization_Is_Reported()
    {
        var shapes = new[] { Rect(0, 0, 100, 100), Rect(0, 0, 100, 100) };
        var r = NestApply.Apply(shapes, 400, 400);

        Assert.True(r.Ok, r.Error);
        Assert.True(r.Utilization > 0, "utilization was not computed");
        Assert.True(r.Utilization <= 1.0001, $"utilization {r.Utilization} exceeds 100%");
    }

    [Fact]
    public void Larger_Spacing_Pushes_Parts_Further_Apart()
    {
        var tight = new[] { Rect(0, 0, 80, 80), Rect(0, 0, 80, 80) };
        var loose = new[] { Rect(0, 0, 80, 80), Rect(0, 0, 80, 80) };

        NestApply.Apply(tight, 600, 400, spacingMm: 1);
        NestApply.Apply(loose, 600, 400, spacingMm: 25);

        double GapOf(VectorShape[] pair)
        {
            var a = B(pair[0]); var b = B(pair[1]);
            // Horizontal gap if side by side, else vertical.
            double hx = Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX);
            double hy = Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY);
            return Math.Max(hx, hy);
        }

        Assert.True(GapOf(loose) > GapOf(tight),
            $"25mm spacing gap {GapOf(loose):F2} not greater than 1mm gap {GapOf(tight):F2}");
    }

    // ---- refusals with a visible reason ----

    [Fact]
    public void An_Empty_Selection_Does_Nothing_With_A_Reason()
    {
        var r = NestApply.Apply(Array.Empty<VectorShape>(), 600, 400);

        Assert.False(r.Ok);
        Assert.Equal(0, r.Placed);
        Assert.Contains("Select closed shapes", r.Error!);
    }

    [Fact]
    public void A_Sheet_With_No_Size_Is_Refused()
    {
        var r = NestApply.Apply(new[] { Rect(0, 0, 10, 10) }, 0, 0);

        Assert.False(r.Ok);
        Assert.Contains("sheet has no size", r.Error!);
    }

    [Fact]
    public void An_All_Open_Selection_Is_Refused()
    {
        var open = VectorShape.Polyline(
            new List<VectorPoint> { new(0, 0), new(50, 0), new(50, 40) }, closed: false);

        var r = NestApply.Apply(new[] { open }, 600, 400);

        Assert.False(r.Ok);
        Assert.Contains("closed shapes", r.Error!);
    }

    [Fact]
    public void A_Part_Bigger_Than_The_Sheet_Is_Reported_Not_Forced()
    {
        var huge = Rect(0, 0, 5000, 5000);
        var r = NestApply.Apply(new[] { huge }, 600, 400);

        // Either refused outright, or reported as unplaced — never silently placed
        // overhanging the sheet.
        if (r.Ok)
        {
            Assert.True(r.Unplaced > 0 || r.Placed == 0,
                "an oversized part was placed on a sheet it cannot fit");
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
        }
    }

    [Fact]
    public void Circles_Count_As_Closed()
    {
        var c1 = VectorShape.Circle(new VectorPoint(0, 0), 30);
        var c2 = VectorShape.Circle(new VectorPoint(0, 0), 30);

        var r = NestApply.Apply(new[] { c1, c2 }, 600, 400);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Placed);
    }
}
