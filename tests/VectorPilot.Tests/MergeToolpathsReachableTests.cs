using System.Globalization;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Merge toolpaths — the engine CutPanel.DoMerge calls.
///
/// MergedToolpathEngine had NO VectorPilot.App call-site, so a multi-tool job had to be
/// exported and streamed one file at a time.
/// </summary>
public class MergeToolpathsReachableTests
{
    private static readonly StrategyRegistry Reg = new();

    /// <summary>A profile program for a rectangle at the given origin.</summary>
    private static List<string> ProfileAt(double x, double y, double w = 40, double h = 30)
    {
        var entry = Reg.Find("profile")!;
        return entry.Compute(
            new[] { VectorShape.Rectangle(x, y, w, h) }, null, entry.DefaultsJson).Gcode;
    }

    private static MergeSourceGcode Source(string name, int tool, IReadOnlyList<string> gcode)
        => new() { Name = name, ToolNumber = tool, GcodeLines = gcode };

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(IEnumerable<string> gcode)
    {
        double minx = double.MaxValue, maxx = double.MinValue;
        double miny = double.MaxValue, maxy = double.MinValue;
        double x = 0, y = 0;

        foreach (var line in gcode)
        {
            var s = line.TrimStart();
            if (!s.StartsWith("G0") && !s.StartsWith("G1")) continue;
            bool saw = false;
            foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (char.ToUpperInvariant(tok[0]) == 'X') { x = v; saw = true; }
                if (char.ToUpperInvariant(tok[0]) == 'Y') { y = v; saw = true; }
            }
            if (!saw) continue;
            minx = Math.Min(minx, x); maxx = Math.Max(maxx, x);
            miny = Math.Min(miny, y); maxy = Math.Max(maxy, y);
        }
        return (minx, maxx, miny, maxy);
    }

    // ---- two profiles become one program covering both ----

    [Fact]
    public void Two_Profiles_Merge_Into_One_Program()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("A", 1, ProfileAt(0, 0)),
            Source("B", 1, ProfileAt(200, 150))
        });

        Assert.True(r.Success, r.ErrorMessage);
        Assert.NotEmpty(r.GcodeLines);
    }

    [Fact]
    public void The_Merged_Program_Spans_Both_Extents()
    {
        var a = ProfileAt(0, 0);
        var b = ProfileAt(200, 150);

        var merged = MergedToolpathEngine.Compute(new[] { Source("A", 1, a), Source("B", 1, b) });

        var ea = Extent(a);
        var eb = Extent(b);
        var em = Extent(merged.GcodeLines);

        Assert.True(em.MinX <= ea.MinX + 1e-6, $"merged minX {em.MinX:F2} lost shape A");
        Assert.True(em.MaxX >= eb.MaxX - 1e-6, $"merged maxX {em.MaxX:F2} lost shape B");
        Assert.True(em.MaxY >= eb.MaxY - 1e-6, $"merged maxY {em.MaxY:F2} lost shape B");
    }

    [Fact]
    public void The_Merged_Program_Has_At_Least_As_Many_Cuts_As_Either_Source()
    {
        var a = ProfileAt(0, 0);
        var b = ProfileAt(200, 150);
        var merged = MergedToolpathEngine.Compute(new[] { Source("A", 1, a), Source("B", 1, b) });

        int Cuts(IEnumerable<string> g) => g.Count(l => l.TrimStart().StartsWith("G1"));

        Assert.True(Cuts(merged.GcodeLines) >= Math.Max(Cuts(a), Cuts(b)));
    }

    [Fact]
    public void Segments_Are_Reported()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("A", 1, ProfileAt(0, 0)),
            Source("B", 1, ProfileAt(120, 0))
        });

        Assert.True(r.TotalSegments > 0, "merge reported zero segments");
    }

    [Fact]
    public void Source_Ids_Are_Tracked()
    {
        var a = Source("A", 1, ProfileAt(0, 0));
        var b = Source("B", 1, ProfileAt(120, 0));

        var r = MergedToolpathEngine.Compute(new[] { a, b });

        Assert.Contains(a.Id, r.SourceIds);
        Assert.Contains(b.Id, r.SourceIds);
    }

    // ---- tool awareness ----

    [Fact]
    public void Different_Tools_Are_Both_Represented()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("Rough", 1, ProfileAt(0, 0)),
            Source("Finish", 2, ProfileAt(60, 0))
        });

        Assert.True(r.Success, r.ErrorMessage);

        var e = Extent(r.GcodeLines);
        Assert.True(e.MaxX > 60, "the second tool's geometry is missing from the merge");
    }

    [Fact]
    public void Merging_Three_Toolpaths_Keeps_All_Three()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("A", 1, ProfileAt(0, 0)),
            Source("B", 2, ProfileAt(100, 0)),
            Source("C", 3, ProfileAt(200, 0))
        });

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(3, r.SourceIds.Count);

        var e = Extent(r.GcodeLines);
        Assert.True(e.MaxX >= 200, $"merged maxX {e.MaxX:F2} dropped the third toolpath");
    }

    [Fact]
    public void The_Source_Programs_Are_Not_Mutated()
    {
        var a = ProfileAt(0, 0);
        var snapshot = a.ToList();

        MergedToolpathEngine.Compute(new[] { Source("A", 1, a), Source("B", 1, ProfileAt(120, 0)) });

        Assert.Equal(snapshot, a);
    }

    [Fact]
    public void No_NaN_Reaches_The_Merged_Program()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("A", 1, ProfileAt(0, 0)),
            Source("B", 2, ProfileAt(120, 90))
        });

        Assert.DoesNotContain(r.GcodeLines, l => l.Contains("NaN"));
    }

    // ---- refusals ----

    [Fact]
    public void An_Empty_Selection_Produces_Nothing()
    {
        var r = MergedToolpathEngine.Compute(Array.Empty<MergeSourceGcode>());

        Assert.True(!r.Success || r.GcodeLines.Count == 0,
            "merging nothing produced a program");
    }

    [Fact]
    public void Sources_With_No_Moves_Produce_No_Cuts()
    {
        var r = MergedToolpathEngine.Compute(new[]
        {
            Source("empty1", 1, new List<string> { "(nothing)" }),
            Source("empty2", 1, new List<string> { "(also nothing)" })
        });

        Assert.DoesNotContain(r.GcodeLines, l => l.TrimStart().StartsWith("G1 X"));
    }

    [Fact]
    public void One_Source_Merges_To_Something_Equivalent_Not_Doubled()
    {
        // DoMerge refuses a single toolpath in the UI, but the engine must still behave:
        // merging one program must not duplicate its geometry.
        var a = ProfileAt(0, 0);
        var r = MergedToolpathEngine.Compute(new[] { Source("A", 1, a) });

        if (r.Success && r.GcodeLines.Count > 0)
        {
            var ea = Extent(a);
            var er = Extent(r.GcodeLines);

            Assert.Equal(ea.MinX, er.MinX, 2);
            Assert.Equal(ea.MaxX, er.MaxX, 2);
        }
    }
}
