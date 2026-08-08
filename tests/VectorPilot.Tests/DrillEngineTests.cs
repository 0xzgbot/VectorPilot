using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class DrillEngineTests
{
    private static readonly DrillPoint[] ThreeHoles =
    {
        new(0, 0, -10),
        new(10, 0, -10),
        new(20, 5, -10)
    };

    private static DrillParams PeckParams(double peckDepthMm = 2.0) => new()
    {
        CycleType = DrillCycleType.PeckDrill,
        PeckDepthMm = peckDepthMm,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        RetractHeightMm = 5.0,
        SafetyHeightMm = 10.0
    };

    [Fact]
    public void PeckDrill_Three_Holes_Emit_Expected_Pass_Count_And_Cycle_Lines()
    {
        var r = DrillEngine.Compute(ThreeHoles, PeckParams(peckDepthMm: 2.0));

        Assert.Equal(3, r.PointCount);
        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("O=DRILL_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l == "(Tool: 60mm)");
        Assert.Contains(r.GcodeLines, l => l == "(Cycle: Peck Drill)");
        Assert.Equal("M30", r.GcodeLines[^2]);
        Assert.Equal("%", r.GcodeLines[^1]);

        // 10mm depth / 2mm peck = 5 plunges per hole (4 intermediate + 1 final), 3 holes.
        Assert.Equal(15, r.GcodeLines.Count(l => l.StartsWith("G1 Z")));
        // Final full-depth plunge per hole.
        Assert.Equal(3, r.GcodeLines.Count(l => l == "G1 Z-10.000 F300"));
        // Intermediate peck depths present (first peck -2.000, second -4.000).
        Assert.Equal(3, r.GcodeLines.Count(l => l == "G1 Z-2.000 F300"));
        Assert.Equal(3, r.GcodeLines.Count(l => l == "G1 Z-4.000 F300"));
        // 4 intermediate retracts to retractHeight 5.0 per hole.
        Assert.Equal(12, r.GcodeLines.Count(l => l == "G0 Z5.0"));
        // Safety-height rapids: 1 before + 1 after each hole.
        Assert.Equal(6, r.GcodeLines.Count(l => l == "G0 Z10.0"));
        // Position rapids.
        Assert.Equal(3, r.GcodeLines.Count(l => l.StartsWith("G0 X")));
        // Point comments.
        Assert.Equal(3, r.GcodeLines.Count(l => l.StartsWith("(Point")));
        // 30mm total depth / 300mm/min * 60 + 3 * 2s = 12s.
        Assert.Equal(12.0, r.EstimatedTimeSeconds, 3);
    }

    [Fact]
    public void PeckDepth_Controls_Number_Of_Intermediate_Retracts()
    {
        // 10mm depth / 3mm peck = ceil(10/3) = 4 plunges: -3, -6, -9, -10 final.
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, PeckParams(peckDepthMm: 3.0));

        Assert.Equal(4, r.GcodeLines.Count(l => l.StartsWith("G1 Z")));
        Assert.Equal(3, r.GcodeLines.Count(l => l == "G0 Z5.0"));
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-3.000 F300");
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-6.000 F300");
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-9.000 F300");
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-10.000 F300");
    }

    [Fact]
    public void Zero_Peck_Depth_Falls_Back_To_Single_Exact_Plunge()
    {
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, PeckParams(peckDepthMm: 0.0));

        Assert.Single(r.GcodeLines, l => l.StartsWith("G1 Z"));
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-10.000 F300");
        Assert.DoesNotContain(r.GcodeLines, l => l == "G0 Z5.0");
    }

    [Fact]
    public void Dwell_Seconds_Emit_G4_At_Bottom_Of_Hole()
    {
        var p = PeckParams(peckDepthMm: 5.0);
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10, DwellSeconds: 0.25) }, p);

        // 2 pecks: -5 intermediate, then final full depth with dwell.
        Assert.Equal(2, r.GcodeLines.Count(l => l.StartsWith("G1 Z")));
        Assert.Contains(r.GcodeLines, l => l == "G4 P0.25");
    }

    [Fact]
    public void DeepHolePeck_Fully_Retracts_To_Safety_Height()
    {
        var p = new DrillParams
        {
            CycleType = DrillCycleType.DeepHolePeck,
            PeckDepthMm = 2.0,
            PlungeFeedRateMmPerMin = 300,
            RetractHeightMm = 5.0,
            SafetyHeightMm = 10.0
        };
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, p);

        // 5 plunges; intermediate retracts go to safety 10.0, never to retractHeight 5.0.
        Assert.Equal(5, r.GcodeLines.Count(l => l.StartsWith("G1 Z")));
        // 1 pre + 4 intermediate + 1 post = 6 rapids to safety.
        Assert.Equal(6, r.GcodeLines.Count(l => l == "G0 Z10.0"));
        Assert.DoesNotContain(r.GcodeLines, l => l == "G0 Z5.0");
    }

    [Fact]
    public void SpotDrill_Goes_To_15_Percent_With_Default_Dwell()
    {
        var p = new DrillParams { CycleType = DrillCycleType.SpotDrill, PlungeFeedRateMmPerMin = 300 };
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, p);

        // -10 * 0.15 = -1.5; default 0.5s dwell.
        Assert.Contains(r.GcodeLines, l => l == "G1 Z-1.500 F300");
        Assert.Contains(r.GcodeLines, l => l == "G4 P0.5");
    }

    [Fact]
    public void Counterbore_Uses_Default_One_Second_Dwell()
    {
        var p = new DrillParams { CycleType = DrillCycleType.Counterbore, PlungeFeedRateMmPerMin = 300 };
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, p);

        Assert.Contains(r.GcodeLines, l => l == "G1 Z-10.000 F300");
        Assert.Contains(r.GcodeLines, l => l == "G4 P1.0");
    }

    [Fact]
    public void Countersink_Uses_Default_Half_Second_Dwell()
    {
        var p = new DrillParams { CycleType = DrillCycleType.Countersink, PlungeFeedRateMmPerMin = 300 };
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, p);

        Assert.Contains(r.GcodeLines, l => l == "G1 Z-10.000 F300");
        Assert.Contains(r.GcodeLines, l => l == "G4 P0.5");
    }

    [Fact]
    public void Spindle_Rpm_Emits_M3_S_When_Configured()
    {
        var p = PeckParams();
        p.SpindleRpm = 12000;
        var r = DrillEngine.Compute(new[] { new DrillPoint(0, 0, -10) }, p);

        Assert.Contains(r.GcodeLines, l => l == "M3 S12000");
    }

    [Fact]
    public void DrillBank_Generates_RowMajor_Grid_With_Through_Depth()
    {
        var p = new DrillBankParams
        {
            GridCols = 3,
            GridRows = 2,
            SpacingX = 20,
            SpacingY = 25,
            CutDepthMm = 10,
            PlungeFeedRateMmPerMin = 300,
            SafetyHeightMm = 10
        };
        var r = DrillBankEngine.Compute(points: null, p);

        Assert.Equal(6, r.PointCount);
        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("O=DRILL_BANK_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l == "(Drill Bank: 3x2 grid — 6 holes)");
        Assert.Contains(r.GcodeLines, l => l == "(Style: Through)");
        Assert.Contains(r.GcodeLines, l => l == "(Tool: 60mm)");
        // Row-major: hole 4 is the first hole of row 2 (y=25).
        Assert.Contains(r.GcodeLines, l => l == "(Hole 4/6: X0.000 Y25.000)");
        Assert.Contains(r.GcodeLines, l => l == "(Hole 6/6: X40.000 Y25.000)");
        // Through style plunges to full cut depth for all 6 holes.
        Assert.Equal(6, r.GcodeLines.Count(l => l == "G1 Z-10.000 F300"));
        // Estimated time: 6 * 10mm / 300 * 60 + 6 * 2 = 24s.
        Assert.Equal(24.0, r.EstimatedTimeSeconds, 3);
    }

    [Fact]
    public void DrillBank_BradPoint_Seats_Center_Point_At_80_Percent_Depth()
    {
        var p = new DrillBankParams
        {
            GridCols = 2,
            GridRows = 1,
            CutDepthMm = 10,
            PlungeFeedRateMmPerMin = 300,
            Style = DrillBankPointStyle.BradPoint
        };
        var r = DrillBankEngine.Compute(points: null, p);

        Assert.Equal(2, r.PointCount);
        Assert.Contains(r.GcodeLines, l => l == "(Style: Brad-point)");
        Assert.Contains(r.GcodeLines, l => l == "(Brad-point: seats the center point at 8.0mm — full depth 10.0mm)");
        Assert.Equal(2, r.GcodeLines.Count(l => l == "G1 Z-8.000 F300"));
        Assert.DoesNotContain(r.GcodeLines, l => l == "G1 Z-10.000 F300");
    }

    [Fact]
    public void DrillBank_Custom_Points_Override_Generated_Grid()
    {
        var pts = new[] { new DrillPoint(1, 2, -5), new DrillPoint(3, 4, -5) };
        var p = new DrillBankParams { PlungeFeedRateMmPerMin = 300, CutDepthMm = 5 };
        var r = DrillBankEngine.Compute(pts, p);

        Assert.Equal(2, r.PointCount);
        Assert.Contains(r.GcodeLines, l => l == "(Hole 2/2: X3.000 Y4.000)");
        // Depth always comes from params.CutDepthMm (Swift behavior), not point.zDepthMm.
        Assert.Equal(2, r.GcodeLines.Count(l => l == "G1 Z-5.000 F300"));
    }
}
