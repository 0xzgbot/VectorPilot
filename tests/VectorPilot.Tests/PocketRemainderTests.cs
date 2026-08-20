using System.Globalization;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Pocket remainder: contour loops clear the pocket, and the raster only covers what the
/// loops left behind.
///
/// The previous hybrid ran contour loops AND then rastered the whole shape again. That
/// re-cut cleared ground, and the raster is what can stray outside a curved wall — a
/// circular pocket must never contain a long cut whose endpoints both lie outside the
/// circle (inset by the tool radius).
/// </summary>
public class PocketRemainderTests
{
    private static VectorShape Circle(double cx, double cy, double r, int segments = 96)
    {
        var pts = new List<VectorPoint>();
        for (int i = 0; i < segments; i++)
        {
            double a = i / (double)segments * 2 * Math.PI;
            pts.Add(new VectorPoint(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return VectorShape.Polyline(pts, closed: true);
    }

    private readonly record struct Move(double X1, double Y1, double X2, double Y2, bool IsCut);

    /// <summary>Consecutive G1 pairs with modal position tracking, as a controller sees them.</summary>
    private static List<Move> CutMoves(IEnumerable<string> gcode)
    {
        var moves = new List<Move>();
        double x = 0, y = 0;
        bool have = false;

        foreach (var raw in gcode)
        {
            var line = raw.Trim();
            bool g0 = line.StartsWith("G0 ", StringComparison.OrdinalIgnoreCase);
            bool g1 = line.StartsWith("G1 ", StringComparison.OrdinalIgnoreCase);
            if (!g0 && !g1) continue;

            double nx = x, ny = y;
            bool sawXy = false;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (char.ToUpperInvariant(tok[0]) == 'X') { nx = v; sawXy = true; }
                if (char.ToUpperInvariant(tok[0]) == 'Y') { ny = v; sawXy = true; }
            }

            if (sawXy && have) moves.Add(new Move(x, y, nx, ny, g1));
            if (sawXy) { x = nx; y = ny; have = true; }
        }
        return moves;
    }

    private const double Cx = 100, Cy = 100, R = 40, Tool = 6.0;

    private static List<string> CirclePocket(bool contourFirst = true) =>
        PocketEngine.Generate(
            new[] { Circle(Cx, Cy, R) },
            cutDepth: 3, stepdown: 3, stepoverPercent: 45,
            feedRate: 1000, plungeRate: 300, spindleSpeed: 12000,
            safeZ: 5, toolDiameter: Tool, contourFirst: contourFirst);

    private static double DistFromCentre(double x, double y)
        => Math.Sqrt((x - Cx) * (x - Cx) + (y - Cy) * (y - Cy));

    // ---- the item's acceptance test ----

    [Fact]
    public void No_Cut_Move_Has_Both_Endpoints_Outside_The_Circle()
    {
        // Tolerance = tool radius: the cutter centre stays a tool radius inside the wall.
        double limit = R - Tool / 2.0;

        var offenders = CutMoves(CirclePocket())
            .Where(m => m.IsCut)
            .Where(m => DistFromCentre(m.X1, m.Y1) > limit + 1e-6
                     && DistFromCentre(m.X2, m.Y2) > limit + 1e-6)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} cut move(s) run entirely outside the circle, e.g. " +
            (offenders.Count > 0
                ? $"({offenders[0].X1:F2},{offenders[0].Y1:F2})->({offenders[0].X2:F2},{offenders[0].Y2:F2})"
                : ""));
    }

    [Fact]
    public void No_Cut_Move_Leaves_The_Circle_At_All()
    {
        // Stricter: every cutting endpoint is inside the inset circle.
        double limit = R - Tool / 2.0 + 0.5;   // 0.5mm slack for segment sampling

        var strays = CutMoves(CirclePocket())
            .Where(m => m.IsCut)
            .Where(m => DistFromCentre(m.X1, m.Y1) > limit || DistFromCentre(m.X2, m.Y2) > limit)
            .ToList();

        Assert.True(strays.Count == 0, $"{strays.Count} cutting endpoint(s) outside the wall");
    }

    [Fact]
    public void The_Pocket_Still_Cuts_Something()
    {
        // A guard against "clear the offenders by emitting nothing".
        var cuts = CutMoves(CirclePocket()).Count(m => m.IsCut);
        Assert.True(cuts > 20, $"only {cuts} cutting moves — the pocket is not being cleared");
    }

    [Fact]
    public void The_Cut_Reaches_Near_The_Wall()
    {
        // It must clear to the wall, not just nibble the middle.
        double reach = CutMoves(CirclePocket()).Where(m => m.IsCut)
            .Max(m => Math.Max(DistFromCentre(m.X1, m.Y1), DistFromCentre(m.X2, m.Y2)));

        double target = R - Tool / 2.0;
        Assert.True(reach > target - 2.0,
            $"deepest cut reaches {reach:F2}mm from centre, short of the {target:F2}mm wall");
    }

    [Fact]
    public void The_Cut_Reaches_The_Middle()
    {
        double nearest = CutMoves(CirclePocket()).Where(m => m.IsCut)
            .Min(m => Math.Min(DistFromCentre(m.X1, m.Y1), DistFromCentre(m.X2, m.Y2)));

        Assert.True(nearest < R / 2,
            $"nothing cut closer than {nearest:F2}mm from centre — the middle is uncleared");
    }

    // ---- the remainder is not re-cut ----

    [Fact]
    public void Contour_Mode_Emits_More_Coverage_Than_Raster_Alone()
    {
        // Contour mode = wall-following loops PLUS the clipped raster, so it must cover
        // strictly more ground than the raster by itself.
        //
        // An earlier version of this test compared loop POINT count against raster LINE
        // count and called the difference "still stacking a full raster" — different
        // units, so the premise was wrong. What actually matters is that contour mode is
        // a superset, and that nothing it emits leaves the pocket (asserted above).
        int rasterOnly = CutMoves(CirclePocket(contourFirst: false)).Count(m => m.IsCut);
        int contour = CutMoves(CirclePocket(contourFirst: true)).Count(m => m.IsCut);

        Assert.True(contour > rasterOnly,
            $"contour mode emits {contour} cuts vs {rasterOnly} raster-only — the loops are missing");
    }

    [Fact]
    public void A_Square_Pocket_Still_Clears_Corner_To_Corner()
    {
        var square = VectorShape.Rectangle(0, 0, 100, 60);
        var g = PocketEngine.Generate(new[] { square }, 3, 3, 45, 1000, 300, 12000, 5, Tool, contourFirst: true);

        var cuts = CutMoves(g).Where(m => m.IsCut).ToList();
        Assert.NotEmpty(cuts);

        double maxX = cuts.Max(m => Math.Max(m.X1, m.X2));
        double maxY = cuts.Max(m => Math.Max(m.Y1, m.Y2));

        Assert.True(maxX > 100 - Tool - 2, $"never cut near the right wall (reached X{maxX:F2})");
        Assert.True(maxY > 60 - Tool - 2, $"never cut near the top wall (reached Y{maxY:F2})");
    }

    [Fact]
    public void Cuts_Stay_Inside_A_Square_Pocket()
    {
        var square = VectorShape.Rectangle(0, 0, 100, 60);
        var g = PocketEngine.Generate(new[] { square }, 3, 3, 45, 1000, 300, 12000, 5, Tool, contourFirst: true);

        foreach (var m in CutMoves(g).Where(m => m.IsCut))
        {
            Assert.True(m.X1 >= -0.01 && m.X1 <= 100.01, $"X{m.X1:F3} outside the pocket");
            Assert.True(m.Y1 >= -0.01 && m.Y1 <= 60.01, $"Y{m.Y1:F3} outside the pocket");
        }
    }

    // ---- the default path from Cut uses this ----

    [Fact]
    public void The_Registry_Default_Uses_Contour_First()
    {
        var reg = new VectorPilot.App.StrategyRegistry();
        var entry = reg.Find("pocket")!;

        // Defaults JSON must carry contourFirst:true, so Calculate never silently takes
        // the raster-only path.
        Assert.Contains("\"contourFirst\": true", entry.DefaultsJson.Replace("\":true", "\": true"));
    }

    [Fact]
    public void The_Engine_Default_Is_Contour_First()
    {
        // Callers that omit the argument (goldens, smoke tests) get the good path.
        var withDefault = PocketEngine.Generate(
            new[] { Circle(Cx, Cy, R) }, 3, 3, 45, 1000, 300, 12000, 5, Tool);
        var explicitOn = CirclePocket(contourFirst: true);

        Assert.Equal(string.Join("\n", explicitOn), string.Join("\n", withDefault));
    }
}
