using System.Globalization;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// V-carve medial axis. Depth already followed local width; the gap was the CENTERLINE —
/// the engine sampled depth along the input path, so the middle of a closed shape was
/// never visited at all.
///
/// The item's fixture: a dumbbell (two wide bulbs joined by a narrow neck). The bulbs must
/// cut DEEPER than the neck, and the toolpath must visit the neck rather than only tracing
/// the outline.
/// </summary>
public class VCarveMedialAxisTests
{
    /// <summary>Two 30mm-radius bulbs at x=40 and x=160, joined by a 12mm-wide neck.</summary>
    private static List<VectorPoint> Dumbbell()
    {
        var pts = new List<VectorPoint>();
        const double r = 30, neckHalf = 6;
        const double leftC = 40, rightC = 160, cy = 100;

        // Lower edge: left bulb bottom, neck bottom, right bulb bottom.
        for (int i = 0; i <= 24; i++)
        {
            double a = Math.PI + i / 24.0 * (Math.PI / 2 + Math.PI / 6);
            pts.Add(new VectorPoint(leftC + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        pts.Add(new VectorPoint(leftC + 20, cy - neckHalf));
        pts.Add(new VectorPoint(rightC - 20, cy - neckHalf));
        for (int i = 0; i <= 24; i++)
        {
            double a = -Math.PI / 3 + i / 24.0 * (Math.PI / 3 + Math.PI / 2 + Math.PI / 3);
            pts.Add(new VectorPoint(rightC + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        pts.Add(new VectorPoint(rightC - 20, cy + neckHalf));
        pts.Add(new VectorPoint(leftC + 20, cy + neckHalf));
        for (int i = 0; i <= 24; i++)
        {
            double a = 2 * Math.PI / 3 + i / 24.0 * (Math.PI / 3);
            pts.Add(new VectorPoint(leftC + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return pts;
    }

    private static VectorShape DumbbellShape()
        => VectorShape.Polyline(Dumbbell(), closed: true);

    private readonly record struct Cut(double X, double Y, double Z);

    private static List<Cut> Cuts(IEnumerable<string> gcode)
    {
        var cuts = new List<Cut>();
        double x = 0, y = 0, z = 0;
        foreach (var raw in gcode)
        {
            var line = raw.Trim();
            if (!line.StartsWith("G1 ", StringComparison.OrdinalIgnoreCase)) continue;
            bool sawXy = false;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                switch (char.ToUpperInvariant(tok[0]))
                {
                    case 'X': x = v; sawXy = true; break;
                    case 'Y': y = v; sawXy = true; break;
                    case 'Z': z = v; break;
                }
            }
            if (sawXy) cuts.Add(new Cut(x, y, z));
        }
        return cuts;
    }

    private static VCarveResult Carve(bool medialAxis = true) =>
        VCarveEngine.Compute(new[] { DumbbellShape() }, new VCarveParams
        {
            VBitAngleDegrees = 90,
            MaxDepthOfCutMm = 20,
            FeedRateMmPerMin = 1000,
            SpindleRpm = 12000,
            StepOverMm = 2,
            MedialAxisPass = medialAxis,
            MedialAxisCellMm = 1.5
        });

    // ---- the item's acceptance criteria ----

    [Fact]
    public void The_Bulbs_Cut_Deeper_Than_The_Neck()
    {
        var cuts = Cuts(Carve().GcodeLines).Where(c => c.Z < -0.01).ToList();
        Assert.NotEmpty(cuts);

        // Bulb centres are at x=40 and x=160; the neck spans roughly x=60..140.
        var bulb = cuts.Where(c => c.X < 55 || c.X > 145).ToList();
        var neck = cuts.Where(c => c.X > 75 && c.X < 125).ToList();

        Assert.NotEmpty(bulb);
        Assert.NotEmpty(neck);

        double deepestBulb = bulb.Min(c => c.Z);   // most negative
        double deepestNeck = neck.Min(c => c.Z);

        Assert.True(deepestBulb < deepestNeck,
            $"bulb deepest {deepestBulb:F3} is not deeper than neck deepest {deepestNeck:F3}");
    }

    [Fact]
    public void The_Toolpath_Visits_The_Neck_Interior()
    {
        // Not just the outline: something must cut near the neck's centre line (y=100),
        // where the outline itself is +/-6mm away.
        var interior = Cuts(Carve().GcodeLines)
            .Where(c => c.Z < -0.01)
            .Where(c => c.X > 75 && c.X < 125 && Math.Abs(c.Y - 100) < 3)
            .ToList();

        Assert.NotEmpty(interior);
    }

    [Fact]
    public void The_Toolpath_Visits_The_Bulb_Interior()
    {
        // The deepest point of a 30mm bulb is its centre, 30mm from any wall.
        var interior = Cuts(Carve().GcodeLines)
            .Where(c => c.Z < -0.01)
            .Where(c => Math.Sqrt((c.X - 40) * (c.X - 40) + (c.Y - 100) * (c.Y - 100)) < 12)
            .ToList();

        Assert.NotEmpty(interior);
    }

    [Fact]
    public void Without_The_Medial_Pass_The_Interior_Is_Never_Reached()
    {
        // This is the bug: outline-only carving cannot reach the middle.
        var outlineOnly = Cuts(Carve(medialAxis: false).GcodeLines)
            .Where(c => c.Z < -0.01)
            .Where(c => Math.Sqrt((c.X - 40) * (c.X - 40) + (c.Y - 100) * (c.Y - 100)) < 12)
            .ToList();

        Assert.Empty(outlineOnly);
    }

    [Fact]
    public void The_Medial_Pass_Adds_Cutting_Moves()
    {
        int with = Cuts(Carve(medialAxis: true).GcodeLines).Count;
        int without = Cuts(Carve(medialAxis: false).GcodeLines).Count;

        Assert.True(with > without, $"medial pass added nothing ({with} vs {without})");
    }

    // ---- the skeleton itself ----

    [Fact]
    public void A_Circle_Skeleton_Peaks_At_Its_Centre()
    {
        var circle = new List<VectorPoint>();
        for (int i = 0; i < 72; i++)
        {
            double a = i / 72.0 * 2 * Math.PI;
            circle.Add(new VectorPoint(100 + Math.Cos(a) * 40, 100 + Math.Sin(a) * 40));
        }

        var skeleton = MedialAxis.Compute(circle, cellMm: 1.0);

        Assert.False(skeleton.IsEmpty);
        Assert.True(Math.Abs(skeleton.MaxClearanceMm - 40) < 3,
            $"max clearance {skeleton.MaxClearanceMm:F2} should be ~40mm (the radius)");
    }

    [Fact]
    public void A_Long_Channel_Skeleton_Runs_Along_Its_Length()
    {
        // 200x20 slot: the spine is a horizontal line, so the longest ridge path must be
        // much wider than it is tall.
        var slot = new List<VectorPoint>
        {
            new(0, 0), new(200, 0), new(200, 20), new(0, 20)
        };

        var skeleton = MedialAxis.Compute(slot, cellMm: 1.0);
        Assert.False(skeleton.IsEmpty);

        var longest = skeleton.Paths[0];
        double w = longest.Max(p => p.Position.X) - longest.Min(p => p.Position.X);
        double h = longest.Max(p => p.Position.Y) - longest.Min(p => p.Position.Y);

        Assert.True(w > h * 3, $"spine spans {w:F1}x{h:F1} — not running along the channel");
    }

    [Fact]
    public void Clearance_Is_Larger_In_A_Wide_Region()
    {
        var skeleton = MedialAxis.Compute(Dumbbell(), cellMm: 1.5);
        Assert.False(skeleton.IsEmpty);

        var all = skeleton.Paths.SelectMany(p => p).ToList();
        var bulb = all.Where(p => p.Position.X < 55).ToList();
        var neck = all.Where(p => p.Position.X > 75 && p.Position.X < 125).ToList();

        Assert.NotEmpty(bulb);
        Assert.NotEmpty(neck);
        Assert.True(bulb.Max(p => p.ClearanceMm) > neck.Max(p => p.ClearanceMm),
            "the bulb is not measured as wider than the neck");
    }

    [Fact]
    public void Every_Ridge_Point_Is_Inside_The_Shape()
    {
        var outline = Dumbbell();
        foreach (var p in MedialAxis.Compute(outline, 1.5).Paths.SelectMany(x => x))
            Assert.True(MedialAxis.PointInPolygon(p.Position, outline),
                $"ridge point ({p.Position.X:F2},{p.Position.Y:F2}) is outside the shape");
    }

    [Fact]
    public void A_Degenerate_Outline_Yields_No_Skeleton()
    {
        Assert.True(MedialAxis.Compute(new List<VectorPoint> { new(0, 0), new(1, 1) }).IsEmpty);
        Assert.True(MedialAxis.Compute(new List<VectorPoint>()).IsEmpty);
    }

    [Fact]
    public void An_Open_Path_Skips_The_Medial_Pass()
    {
        // An open path has no interior to skeletonise; it must still carve normally.
        var open = VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(50, 0), new(100, 0)
        }, closed: false);

        var r = VCarveEngine.Compute(new[] { open }, new VCarveParams
        {
            VBitAngleDegrees = 90, MaxDepthOfCutMm = 5, FeedRateMmPerMin = 800, MedialAxisPass = true
        });

        Assert.DoesNotContain(r.GcodeLines, l => l.Contains("Medial axis"));
    }
}
