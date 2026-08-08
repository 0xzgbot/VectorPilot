using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class HeightfieldToolpathTests
{
    /// <summary>8x8 grid: 4mm plateau with a 2x2 valley (rows/cols 3-4) at z=0.</summary>
    private static HeightfieldData Valley()
    {
        var heights = new double[64];
        Array.Fill(heights, 4.0);
        for (int r = 3; r <= 4; r++)
            for (int c = 3; c <= 4; c++)
                heights[r * 8 + c] = 0;
        return new HeightfieldData(8, 8, 1.0, 0, 0, heights);
    }

    [Fact]
    public void Rough_Emits_ZLevel_Passes_And_Header()
    {
        var p = new HeightfieldRoughParams { StepDownMm = 1.0, StepOverMm = 1.0, StockAllowanceMm = 0.5, FeedRateMmPerMin = 1000 };
        var r = HeightfieldRoughEngine.Compute(Valley(), p);
        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("O=ROUGH_3D", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l == "M30");
        // stockTop=4.5 → levels 3.5, 2.5, 1.5, 0.5, 0 → 5 passes
        Assert.Equal(5, r.PassCount);
        Assert.True(r.EstimatedTimeSeconds > 0);
    }

    [Fact]
    public void Rough_Cuts_Contiguous_Runs_Per_Row()
    {
        var p = new HeightfieldRoughParams { StepDownMm = 5.0, StepOverMm = 1.0, StockAllowanceMm = 0.5 };
        var r = HeightfieldRoughEngine.Compute(Valley(), p);
        // stockTop=4.5, first level 4.5-5 < 0 → only the floor level [0]. One pass.
        Assert.Equal(1, r.PassCount);
        // Rows 3 and 4 each cut the contiguous cols 3-4 run (G0 to 3.5, G1 to 4.5).
        Assert.Contains(r.GcodeLines, l => l == "G0 X3.500 Y3.500");
        Assert.Contains(r.GcodeLines, l => l == "G1 X4.500 Y3.500 F1000");
        Assert.Contains(r.GcodeLines, l => l == "G0 X3.500 Y4.500");
        Assert.Contains(r.GcodeLines, l => l == "G1 X4.500 Y4.500 F1000");
    }

    [Fact]
    public void Rough_Linked_Spindle_Emits_M3()
    {
        var p = new HeightfieldRoughParams { StepDownMm = 5.0, SpindleRpm = 12000 };
        var r = HeightfieldRoughEngine.Compute(Valley(), p);
        Assert.Contains(r.GcodeLines, l => l == "M3 S12000");
    }

    [Fact]
    public void Rough_Rest_Pass_Only_Cuts_Narrow_Valleys()
    {
        // Valley run width 2mm < 6mm previous tool → cut.
        var p = new HeightfieldRoughParams { StepDownMm = 5.0, StepOverMm = 1.0, StockAllowanceMm = 0.5, PreviousToolDiameterMm = 6.0 };
        var r = HeightfieldRoughEngine.Compute(Valley(), p);
        Assert.Contains(r.GcodeLines, l => l == "G1 X4.500 Y3.500 F1000");

        // Previous tool 1.0mm (narrower than the 2mm run) → run was already cleared → no cut.
        var p2 = new HeightfieldRoughParams { StepDownMm = 5.0, StepOverMm = 1.0, StockAllowanceMm = 0.5, PreviousToolDiameterMm = 1.0 };
        var r2 = HeightfieldRoughEngine.Compute(Valley(), p2);
        Assert.DoesNotContain(r2.GcodeLines, l => l.StartsWith("G1 X4.500"));
    }

    [Fact]
    public void Finish_Follows_Surface_With_Z_Variation()
    {
        var heights = new double[64];
        for (int i = 0; i < 64; i++) heights[i] = 4.0;
        // A bump: cell (4,4) at z=6.
        heights[4 * 8 + 4] = 6.0;
        var hf = new HeightfieldData(8, 8, 1.0, 0, 0, heights);

        var p = new HeightfieldFinishParams { StepOverMm = 1.0, ToolDiameterMm = 3.175 };
        var r = HeightfieldFinishEngine.Compute(hf, p);
        Assert.Equal("O=FINISH_3D", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 X") && l.Contains("Z"));
        // stockTop = 6 → surface Z = h - 6; plateau cells → Z=-2.0 appears.
        Assert.Contains(r.GcodeLines, l => l.Contains("Z-2.000"));
        Assert.Equal(8, r.PassCount); // 8 rows at stepOver 1.0
    }

    [Fact]
    public void RoughEstimator_Validates_And_Estimates()
    {
        var p = new RoughToolpathParams { StepOverMm = 0.5, StepDownMm = 0.25, ToolDiameterMm = 6.0 };
        var (valid, errors) = RoughToolpathEngine.Validate(p);
        Assert.True(valid);
        Assert.Empty(errors);

        var bad = new RoughToolpathParams { StepOverMm = 10, ToolDiameterMm = 6.0 };
        var r = RoughToolpathEngine.Generate(bad, 0, 10, 100, 100);
        Assert.False(r.Success);
        Assert.Contains("exceeds", r.ErrorMessage);

        var ok = RoughToolpathEngine.Generate(p, 0, 10, 100, 100);
        Assert.True(ok.Success);
        Assert.True(ok.EstimatedTimeMinutes > 0);
        Assert.True(ok.ToolChanges >= 1);
    }
}
