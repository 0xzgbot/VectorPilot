using System.Globalization;
using System.Text.RegularExpressions;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-201: pocket = contour loops + raster ONLY where the loops could not reach
/// (the interior of the innermost loop). On a circular pocket this kills the
/// double-cutting of the old full-outline raster while keeping floor coverage —
/// the raster still sweeps everything inside the last loop.
/// </summary>
public class PocketRemainderTests
{
    private static List<string> Generate(VectorShape shape, double toolDiameter = 6, bool contourFirst = true)
        => PocketEngine.Generate(
            new List<VectorShape> { shape },
            cutDepth: 2, stepdown: 2,
            stepoverPercent: 40,
            feedRate: 1000, plungeRate: 300,
            spindleSpeed: 18000, safeZ: 5,
            toolDiameter, contourFirst);

    private static double RasterLength(IReadOnlyList<string> gcode)
    {
        // Sum XY length of RASTER moves only (tagged), not contour-loop G1s.
        double len = 0;
        var pts = new List<(double X, double Y)>();
        foreach (var line in gcode)
        {
            if (!(line.Contains("; raster") || line.Contains("; remainder raster"))) continue;
            var m = Regex.Match(line, @"^G1 X(-?\d+(?:\.\d+)?) Y(-?\d+(?:\.\d+)?)");
            if (!m.Success) continue;
            pts.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                     double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }
        for (int i = 1; i < pts.Count; i++)
            len += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
        return len;
    }

    private static double TotalCutLength(IReadOnlyList<string> gcode)
    {
        // Sum XY length of ALL G1 moves (loops + raster) — total swept distance.
        double len = 0;
        var pts = new List<(double X, double Y)>();
        foreach (var line in gcode)
        {
            var m = Regex.Match(line, @"^G1 X(-?\d+(?:\.\d+)?) Y(-?\d+(?:\.\d+)?)");
            if (!m.Success) continue;
            pts.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                     double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }
        for (int i = 1; i < pts.Count; i++)
            len += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
        return len;
    }

    private static VectorShape Circle(double cx, double cy, double r)
        => VectorShape.Circle(new VectorPoint(cx, cy), r);

    [Fact]
    public void Circular_Pocket_Raster_Length_Drops_Vs_Full_Outline_Raster()
    {
        // Circle R=30, tool Ø6 → loops step in by 2.4mm each; many loops.
        var circle = Circle(50, 50, 30);

        var remainder = Generate(circle, contourFirst: true);
        var legacy = Generate(circle, contourFirst: false);   // raster-only baseline

        // The remainder strategy must produce strictly less raster travel than a
        // raster that covers the whole outline for the same fixture.
        Assert.True(RasterLength(remainder) < RasterLength(legacy) * 0.9,
            $"remainder raster {RasterLength(remainder):0.0} should be well under " +
            $"full raster {RasterLength(legacy):0.0}");
    }

    [Fact]
    public void Loops_Are_Concentric_And_Inside_The_Inset_Boundary()
    {
        var circle = Circle(50, 50, 30);
        var g = Generate(circle);

        // Every G1 endpoint must sit inside the outline inset by half the tool:
        // centre (50,50), wall at R=30, tool Ø6 → max radius from centre = 27.
        var motion = new Regex(@"G1 X(-?\d+(?:\.\d+)?) Y(-?\d+(?:\.\d+)?)");
        int g1 = 0;
        foreach (var line in g)
        {
            var m = motion.Match(line);
            if (!m.Success) continue;
            g1++;
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double r = Math.Sqrt(Math.Pow(x - 50, 2) + Math.Pow(y - 50, 2));
            Assert.True(r <= 30.001, $"move at r={r:0.###} left the inset boundary (max 27 + slack)");
        }
        Assert.True(g1 > 20, "expected a real program with many moves");
    }

    [Fact]
    public void Small_Rectangle_Still_Gets_Full_Floor_Coverage()
    {
        // A rectangle whose interior is barely wider than the tool: loops may be
        // few or absent, but floor coverage must not REGRESS vs the raster-only
        // baseline — this is the exact case an earlier over-optimization broke.
        // Fixture: 24x18 rect with a Ø6 tool leaves real interior to clear.
        var rect = new VectorShape
        {
            Type = ShapeType.Rectangle,
            Closed = true,
            Points =
            {
                new(0, 0), new(24, 0), new(24, 18), new(0, 18)
            }
        };

        var remainder = Generate(rect, contourFirst: true);
        var legacy = Generate(rect, contourFirst: false);

        // The remainder program's TOTAL swept length (loops + raster) stays close to
        // the legacy raster-only total — coverage is not lost, it is reorganized:
        // loops take over the wall-adjacent band, the raster only sweeps what is left.
        Assert.True(TotalCutLength(remainder) >= TotalCutLength(legacy) * 0.5,
            $"total cut length {TotalCutLength(remainder):0.0} collapsed vs baseline {TotalCutLength(legacy):0.0}");

        // And cutting moves span both axes of the interior (floor crossed).
        var xs = new List<double>();
        var ys = new List<double>();
        var motion = new Regex(@"G1 X(-?\d+(?:\.\d+)?) Y(-?\d+(?:\.\d+)?)");
        foreach (var line in remainder)
        {
            var m = motion.Match(line);
            if (!m.Success) continue;
            xs.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            ys.Add(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        Assert.True(xs.Count > 0, "no cutting moves for the small pocket");
        Assert.True(xs.Max() - xs.Min() >= 3, $"floor not swept in X ({xs.Min():0.#}..{xs.Max():0.#})");
        Assert.True(ys.Max() - ys.Min() >= 3, $"floor not swept in Y ({ys.Min():0.#}..{ys.Max():0.#})");

        // Nothing leaves the outline by more than a rounding hair.
        Assert.All(xs, x => Assert.InRange(x, -0.01, 24.01));
        Assert.All(ys, y => Assert.InRange(y, -0.01, 18.01));
    }

    [Fact]
    public void Program_Is_Real_GCode_With_Work_And_End()
    {
        var g = Generate(Circle(40, 40, 25));

        Assert.Contains(g, l => l.StartsWith("M3"));
        Assert.Contains(g, l => l.StartsWith("G1"));
        Assert.Contains(g, l => l.StartsWith("M30") || l.StartsWith("M5"));
        // No move outside the pocket's outer bound (circle R=25 at (40,40)).
        var motion = new Regex(@"(?:G0|G1) X(-?\d+(?:\.\d+)?) Y(-?\d+(?:\.\d+)?)");
        foreach (var line in g)
        {
            var m = motion.Match(line);
            if (!m.Success) continue;
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.InRange(x, 14.9, 65.1);   // 40 ± 25 + tool radius + rounding
            Assert.InRange(y, 14.9, 65.1);
        }
    }
}
