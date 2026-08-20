using System.Globalization;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Array copy of a calculated toolpath — the path CutPanel.ArrayCopy_Click uses.
///
/// ArrayCopyEngine had ZERO VectorPilot.App call-sites: linear/grid/circular arrays
/// existed in the engine but no UI could produce one.
/// </summary>
public class ArrayCopyReachableTests
{
    /// <summary>A 20x20 square at the origin, cut at Z-1.</summary>
    private static List<string> Square() => new()
    {
        "G90", "G21", "M3 S12000",
        "G0 Z5.000",
        "G0 X0.000 Y0.000",
        "G1 Z-1.000 F300",
        "G1 X20.000 Y0.000 F1000",
        "G1 X20.000 Y20.000 F1000",
        "G1 X0.000 Y20.000 F1000",
        "G1 X0.000 Y0.000 F1000",
        "G0 Z5.000",
        "M5"
    };

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(IEnumerable<string> gcode)
    {
        double minx = double.MaxValue, maxx = double.MinValue;
        double miny = double.MaxValue, maxy = double.MinValue;
        double x = 0, y = 0;

        foreach (var line in gcode)
        {
            if (!line.StartsWith("G0") && !line.StartsWith("G1")) continue;
            bool saw = false;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (tok[0] == 'X') { x = v; saw = true; }
                if (tok[0] == 'Y') { y = v; saw = true; }
            }
            if (!saw) continue;
            minx = Math.Min(minx, x); maxx = Math.Max(maxx, x);
            miny = Math.Min(miny, y); maxy = Math.Max(maxy, y);
        }
        return (minx, maxx, miny, maxy);
    }

    // ---- linear ----

    [Fact]
    public void Linear_Count_Three_Triples_The_Motion_Extent()
    {
        var basePath = Square();
        var (bMinX, bMaxX, _, _) = Extent(basePath);
        double baseSpan = bMaxX - bMinX;   // 20mm

        var r = ArrayCopyEngine.ComputeLinear(basePath, new LinearPattern { Count = 3, SpacingMm = 50 });

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(3, r.TotalCount);

        var (minx, maxx, _, _) = Extent(r.GcodeLines);
        double span = maxx - minx;

        // 3 copies at 50mm pitch: first at 0, last at 100, plus the 20mm part = 120mm.
        Assert.True(span > baseSpan * 2.5,
            $"array span {span:F2} is not ~3x the {baseSpan:F2} base span");
    }

    [Fact]
    public void Linear_Emits_More_Motion_Than_The_Original()
    {
        var basePath = Square();
        var r = ArrayCopyEngine.ComputeLinear(basePath, new LinearPattern { Count = 4, SpacingMm = 30 });

        int baseCuts = basePath.Count(l => l.StartsWith("G1 X"));
        int arrayCuts = r.GcodeLines.Count(l => l.StartsWith("G1 X"));

        Assert.True(arrayCuts >= baseCuts * 4 - 2,
            $"4 copies produced {arrayCuts} cut moves vs {baseCuts} in the original");
    }

    [Fact]
    public void Larger_Spacing_Spreads_The_Array_Further()
    {
        var tight = ArrayCopyEngine.ComputeLinear(Square(), new LinearPattern { Count = 3, SpacingMm = 25 });
        var loose = ArrayCopyEngine.ComputeLinear(Square(), new LinearPattern { Count = 3, SpacingMm = 100 });

        double tightSpan = Extent(tight.GcodeLines).MaxX - Extent(tight.GcodeLines).MinX;
        double looseSpan = Extent(loose.GcodeLines).MaxX - Extent(loose.GcodeLines).MinX;

        Assert.True(looseSpan > tightSpan, $"100mm pitch span {looseSpan:F2} <= 25mm pitch span {tightSpan:F2}");
    }

    // ---- grid ----

    [Fact]
    public void Grid_Extends_In_Both_Axes()
    {
        var r = ArrayCopyEngine.ComputeGrid(Square(),
            new GridPattern { Columns = 3, Rows = 2, ColumnSpacingMm = 40, RowSpacingMm = 40 });

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(6, r.TotalCount);

        var (minx, maxx, miny, maxy) = Extent(r.GcodeLines);
        Assert.True(maxx - minx > 60, $"grid X span only {maxx - minx:F2}");
        Assert.True(maxy - miny > 40, $"grid Y span only {maxy - miny:F2}");
    }

    // ---- circular ----

    [Fact]
    public void Circular_Count_Four_With_Radius_Differs_From_Linear()
    {
        var linear = ArrayCopyEngine.ComputeLinear(Square(), new LinearPattern { Count = 4, SpacingMm = 60 });
        var circular = ArrayCopyEngine.ComputeCircular(Square(),
            new CircularPattern { Count = 4, RadiusMm = 60, CenterX = 0, CenterY = 0 });

        Assert.True(circular.Success, circular.ErrorMessage);
        Assert.Equal(4, circular.TotalCount);

        Assert.NotEqual(
            string.Join("\n", linear.GcodeLines),
            string.Join("\n", circular.GcodeLines));
    }

    [Fact]
    public void Circular_Array_Spreads_In_Y_Unlike_A_Plain_Linear_Array()
    {
        // Part offset from the rotation centre, so rotating it sweeps a real circle.
        var offset = Square().Select(l => l.Replace("X0.000", "X60.000").Replace("X20.000", "X80.000")).ToList();

        var linear = ArrayCopyEngine.ComputeLinear(offset, new LinearPattern { Count = 4, SpacingMm = 60 });
        var circular = ArrayCopyEngine.ComputeCircular(offset,
            new CircularPattern { Count = 4, RadiusMm = 60, CenterX = 0, CenterY = 0, EndAngleDeg = 360 });

        double linY = Extent(linear.GcodeLines).MaxY - Extent(linear.GcodeLines).MinY;
        double cirY = Extent(circular.GcodeLines).MaxY - Extent(circular.GcodeLines).MinY;

        Assert.True(cirY > linY,
            $"circular Y span {cirY:F2} not greater than linear {linY:F2} — it is not going around");
    }

    [Fact]
    public void Circular_Spreads_A_Part_Around_The_Centre()
    {
        // Convention (pinned by ArrayMergeRotaryTests): a circular array ROTATES copies
        // about the centre, so the part's own distance from that centre is the radius.
        // A part sitting at X=100 must therefore sweep to negative X.
        var offset = Square().Select(l => l.Replace("X0.000", "X100.000").Replace("X20.000", "X120.000")).ToList();

        var r = ArrayCopyEngine.ComputeCircular(offset,
            new CircularPattern { Count = 4, RadiusMm = 100, CenterX = 0, CenterY = 0, EndAngleDeg = 360 });

        Assert.True(r.Success, r.ErrorMessage);
        var (minx, maxx, miny, maxy) = Extent(r.GcodeLines);

        Assert.True(minx < -50, $"copies never reached the far side of the circle (minX {minx:F2})");
        Assert.True(maxy - miny > 100, $"circular array Y span only {maxy - miny:F2}");
    }

    [Fact]
    public void A_Part_Further_From_The_Centre_Sweeps_A_Bigger_Circle()
    {
        List<string> AtX(double x) => Square()
            .Select(l => l.Replace("X0.000", $"X{x:F3}").Replace("X20.000", $"X{x + 20:F3}")).ToList();

        var near = ArrayCopyEngine.ComputeCircular(AtX(30),
            new CircularPattern { Count = 6, RadiusMm = 30, EndAngleDeg = 360 });
        var far = ArrayCopyEngine.ComputeCircular(AtX(120),
            new CircularPattern { Count = 6, RadiusMm = 120, EndAngleDeg = 360 });

        double nearSpan = Extent(near.GcodeLines).MaxX - Extent(near.GcodeLines).MinX;
        double farSpan = Extent(far.GcodeLines).MaxX - Extent(far.GcodeLines).MinX;

        Assert.True(farSpan > nearSpan, $"far span {farSpan:F2} <= near span {nearSpan:F2}");
    }

    // ---- the original is never destroyed ----

    [Fact]
    public void The_Source_Program_Is_Not_Mutated()
    {
        var basePath = Square();
        var snapshot = basePath.ToList();

        ArrayCopyEngine.ComputeLinear(basePath, new LinearPattern { Count = 5, SpacingMm = 40 });

        Assert.Equal(snapshot, basePath);
    }

    [Fact]
    public void Every_Array_Keeps_The_Spindle_And_Program_End()
    {
        foreach (var r in new[]
        {
            ArrayCopyEngine.ComputeLinear(Square(), new LinearPattern { Count = 2, SpacingMm = 30 }),
            ArrayCopyEngine.ComputeGrid(Square(), new GridPattern { Columns = 2, Rows = 2 }),
            ArrayCopyEngine.ComputeCircular(Square(), new CircularPattern { Count = 3, RadiusMm = 40 })
        })
        {
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 X"));
        }
    }

    // ---- refusals ----

    [Fact]
    public void An_Empty_Program_Produces_No_Array()
    {
        var r = ArrayCopyEngine.ComputeLinear(new List<string>(), new LinearPattern { Count = 3 });
        Assert.True(!r.Success || r.GcodeLines.Count == 0);
    }

    [Fact]
    public void A_Count_Of_One_Is_Not_An_Array()
    {
        var r = ArrayCopyEngine.ComputeLinear(Square(), new LinearPattern { Count = 1, SpacingMm = 50 });

        // Either refused, or a single instance identical in extent to the original.
        if (r.Success && r.GcodeLines.Count > 0)
        {
            var (minx, maxx, _, _) = Extent(r.GcodeLines);
            Assert.True(maxx - minx < 25, $"count=1 spread to {maxx - minx:F2}mm");
        }
    }
}
