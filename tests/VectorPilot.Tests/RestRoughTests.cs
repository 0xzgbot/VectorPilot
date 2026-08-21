using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-203: rest-rough leftover stock. NO new engine was written for this card — the
/// behaviour already exists in HeightfieldToolpath.cs:
///
///   HeightfieldRoughParams.PreviousToolDiameterMm  (line ~27)
///   IsRestRough => PreviousToolDiameterMm &gt; 1e-9 (line ~29)
///   the per-run skip: a horizontal run of material is machined only when NOT rest mode,
///   or when the run is NARROWER than the previous tool (lines ~116-117).
///
/// The semantics being pinned: a big first tool (e.g. 6mm) cannot enter valleys narrower
/// than itself, so the rest pass with PreviousToolDiameterMm=6 machines ONLY those narrow
/// runs and skips everything at least 6mm wide. PreviousToolDiameterMm=0 is plain full
/// roughing — every run is cut. These tests exist so nobody "simplifies" the skip rule
/// back into full clearing, which would double-cut everything the first tool already took.
/// </summary>
public class RestRoughTests
{
    /// <summary>
    /// A heightfield with two distinct features: one WIDE valley (12mm across — even a 6mm
    /// tool fits inside) and one NARROW slot (2mm across — a 6mm tool cannot enter). Both
    /// sit below the surrounding surface so they read as material to clear.
    /// Grid: 40x20 cells at 1mm; wide valley spans x=4..15, narrow slot x=25..26.
    /// </summary>
    private static HeightfieldData TwoFeatureField()
    {
        int w = 40, h = 20;
        var heights = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Surface at 0 by default; valleys carved DOWN is irrelevant — roughing keys
                // off "cell surface below current level", so make valleys LOW.
                if (x >= 4 && x <= 15) heights[y * w + x] = -3.0;      // wide: 12 cells
                else if (x >= 25 && x <= 26) heights[y * w + x] = -3.0; // narrow: 2 cells
                else heights[y * w + x] = 0.5;
            }
        }
        return new HeightfieldData(w, h, cellSizeMm: 1.0, minX: 0, minY: 0, heights);
    }

    private static HeightfieldRoughParams P(double previousDiameter) => new()
    {
        ToolDiameterMm = 6,
        StepDownMm = 4,           // single z level deeper than the valleys
        StepOverMm = 1.5,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        SafeZHeightMm = 5,
        StockAllowanceMm = 0.5,
        PreviousToolDiameterMm = previousDiameter,
    };

    private static List<string> CutLines(HeightfieldToolpathResult r)
        => r.GcodeLines.Where(l =>
        {
            var s = l.TrimStart();
            return s.StartsWith("G1 X", StringComparison.Ordinal);   // horizontal clearing moves
        }).ToList();

    [Fact]
    public void Zero_Previous_Tool_Matches_Today_Full_Rough()
    {
        // PreviousToolDiameterMm = 0 must behave exactly like the pre-rest-rough engine:
        // every material run gets cleared.
        var field = TwoFeatureField();
        var full = HeightfieldRoughEngine.Compute(field, P(0));

        var cuts = CutLines(full);
        Assert.NotEmpty(cuts);

        // Both the wide valley AND the narrow slot contain G1 X moves.
        Assert.Contains(cuts, l => XOf(l) is >= 4 and <= 16);     // inside the wide valley
        Assert.Contains(cuts, l => XOf(l) is >= 25 and <= 27);    // inside the narrow slot
    }

    [Fact]
    public void Rest_Rough_Skips_Wide_Runs_The_First_Tool_Already_Cleared()
    {
        var field = TwoFeatureField();
        var rest = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 6));

        var cuts = CutLines(rest);
        // The 12mm-wide valley is wider than the 6mm previous tool → the FIRST tool cleared
        // it → the rest pass must leave it alone.
        Assert.DoesNotContain(cuts, l => XOf(l) is >= 4 and <= 16);
    }

    [Fact]
    public void Rest_Rough_Still_Cuts_Narrow_Valleys_The_First_Tool_Could_Not_Enter()
    {
        var field = TwoFeatureField();
        var rest = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 6));

        var cuts = CutLines(rest);
        // The 2mm slot is narrower than the 6mm previous tool → only the rest pass reaches it.
        Assert.Contains(cuts, l => XOf(l) is >= 25 and <= 27);
    }

    [Fact]
    public void Rest_And_Full_Produce_Different_Programs()
    {
        // The card's AC, stated as identity: previous 6mm vs previous 0 must NOT emit the
        // same G-code — otherwise rest mode would be a no-op flag.
        var field = TwoFeatureField();
        string full = string.Join("\n", HeightfieldRoughEngine.Compute(field, P(0)).GcodeLines);
        string rest = string.Join("\n", HeightfieldRoughEngine.Compute(field, P(6)).GcodeLines);

        Assert.NotEqual(full, rest);
        // And concretely: rest clears strictly less material.
        Assert.True(CutLines(HeightfieldRoughEngine.Compute(field, P(6))).Count
                    < CutLines(HeightfieldRoughEngine.Compute(field, P(0))).Count,
            "rest pass should machine fewer runs than a full rough");
    }

    [Fact]
    public void A_Huge_Previous_Tool_Means_Rest_Cuts_Everything()
    {
        // A 50mm "first tool" could not enter ANY of our runs (2mm and 12mm are both
        // narrower than 50), so the rest pass must clear all of them.
        var field = TwoFeatureField();
        var rest = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 50));

        var cuts = CutLines(rest);
        Assert.Contains(cuts, l => XOf(l) is >= 4 and <= 16);     // wide valley
        Assert.Contains(cuts, l => XOf(l) is >= 25 and <= 27);    // narrow slot
    }

    [Fact]
    public void Tiny_Previous_Tool_Clears_Almost_Nothing_And_That_Is_Correct()
    {
        // The skip rule reads: machine a run when runWidth < PreviousToolDiameterMm. A tiny
        // previous tool (0.5mm) already reached everywhere, so NO run qualifies and the rest
        // pass legitimately emits no clearing moves.
        var field = TwoFeatureField();
        var tiny = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 0.5));

        Assert.Empty(CutLines(tiny));
    }

    [Fact]
    public void IsRestRough_Tracks_The_Threshold_Not_Plain_Zero()
    {
        var p = new HeightfieldRoughParams { PreviousToolDiameterMm = 0 };
        Assert.False(p.IsRestRough);

        p.PreviousToolDiameterMm = 0.0000001;   // above the 1e-9 threshold
        Assert.True(p.IsRestRough);

        p.PreviousToolDiameterMm = 6.0;
        Assert.True(p.IsRestRough);
    }

    [Fact]
    public void Boundary_Run_Exactly_The_Previous_Diameter_Is_Skipped()
    {
        // runWidth < Previous - 1e-9 is the skip condition: a run EXACTLY as wide as the
        // previous tool is NOT narrower, so it is skipped (the first tool just fit inside).
        // Build a field whose only feature is an exactly-6mm run.
        int w = 30, h = 10;
        var heights = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                heights[y * w + x] = (x is >= 10 and <= 15) ? -3.0 : 0.5;   // 6 cells = 6mm
        var field = new HeightfieldData(w, h, 1.0, 0, 0, heights);

        var rest = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 6));
        Assert.Empty(CutLines(rest));
    }

    [Fact]
    public void No_NaN_In_Any_Emitted_Line()
    {
        var field = TwoFeatureField();
        foreach (var prev in new[] { 0.0, 0.5, 6.0, 50.0 })
        {
            var result = HeightfieldRoughEngine.Compute(field, P(prev));
            Assert.DoesNotContain(result.GcodeLines, l => l.Contains("NaN", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.GcodeLines, l => l.Contains("Infinity", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Rest_Mode_Still_Respects_Z_Levels_And_Feeds()
    {
        // Rest changes WHERE it cuts, not HOW: plunge feed and cut feed survive intact.
        var field = TwoFeatureField();
        var rest = HeightfieldRoughEngine.Compute(field, P(previousDiameter: 6));

        var lines = rest.GcodeLines;
        Assert.Contains(lines, l => l.Contains("F300", StringComparison.Ordinal));   // plunge
        Assert.Contains(lines, l => l.Contains("F1000", StringComparison.Ordinal));  // cut
    }

    private static double XOf(string g1Line)
    {
        // Lines look like "G1 X12.000 Y5.000 F1000" or "G1 Z..." — extract the X word.
        var i = g1Line.IndexOf('X');
        if (i < 0) return double.NaN;
        int end = i + 1;
        while (end < g1Line.Length && (char.IsDigit(g1Line[end]) || g1Line[end] == '.' || g1Line[end] == '-'))
            end++;
        return double.Parse(g1Line.Substring(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture);
    }
}
