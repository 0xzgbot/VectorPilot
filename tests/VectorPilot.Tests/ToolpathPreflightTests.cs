using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathPreflightTests
{
    private static List<VectorShape> TwoParallelLines(double gap) => new()
    {
        VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(0, 20)),
        VectorShape.Line(new VectorPoint(gap, 0), new VectorPoint(gap, 20))
    };

    [Fact]
    public void R013_Flags_Punch_Through_With_Flat_Depth_Fix()
    {
        var v = new VCarveParams { VBitAngleDegrees = 60, FlatBottomMode = false };
        var issue = ToolpathPreflight.VCarvePunchThrough(v, TwoParallelLines(20), materialThicknessMm: 6);
        Assert.NotNull(issue);
        Assert.Equal("R013", issue!.RuleId);
        Assert.Equal(ToolpathPreflightSeverity.Error, issue.Severity);
        Assert.Equal(ToolpathPreflightFix.FixKind.SetFlatDepth, issue.Fix.Kind);
        Assert.Equal(5.5, issue.Fix.RecommendedMm!.Value, 3); // 6 - 0.5 margin
    }

    [Fact]
    public void R013_Flat_Bottom_Mode_Clears()
    {
        var v = new VCarveParams { VBitAngleDegrees = 60, FlatBottomMode = true };
        Assert.Null(ToolpathPreflight.VCarvePunchThrough(v, TwoParallelLines(20), 6));
    }

    [Fact]
    public void R013_Small_Gap_Is_Fine()
    {
        var v = new VCarveParams { VBitAngleDegrees = 90 };
        Assert.Null(ToolpathPreflight.VCarvePunchThrough(v, TwoParallelLines(2), 6));
    }

    [Fact]
    public void MaxVDepth_Matches_Formula()
    {
        // 90° bit spanning 10mm → depth = 10 / (2·tan(45°)) = 5.
        Assert.Equal(5.0, ToolpathPreflight.MaxVDepth(90, 10), 6);
    }

    [Fact]
    public void R014_Flags_Through_Cut_Without_Hold_Down()
    {
        var p = new ProfileToolpathParams { MaxDepthOfCutMm = 12, TabCount = 0 };
        var issue = ToolpathPreflight.ThroughCutWithoutHoldDown(p, materialThicknessMm: 10, vacuumHoldDown: false);
        Assert.NotNull(issue);
        Assert.Equal("R014", issue!.RuleId);
        Assert.Equal(ToolpathPreflightFix.FixKind.AddTabs, issue.Fix.Kind);

        // Tabs clear it; vacuum clears it; shallow cut clears it.
        p.TabCount = 4;
        Assert.Null(ToolpathPreflight.ThroughCutWithoutHoldDown(p, 10, false));
        p.TabCount = 0;
        Assert.Null(ToolpathPreflight.ThroughCutWithoutHoldDown(p, 10, true));
        var shallow = new ProfileToolpathParams { MaxDepthOfCutMm = 4 };
        Assert.Null(ToolpathPreflight.ThroughCutWithoutHoldDown(shallow, 10, false));
    }

    [Fact]
    public void KeepOutZone_Violation_Warns_On_Cut_Segment()
    {
        var zone = new KeepOutZone
        {
            Name = "Clamp",
            Type = KeepOutZoneType.Rectangle,
            RectMinX = 4, RectMinY = 4, RectMaxX = 6, RectMaxY = 6
        };
        var gcode = new List<string> { "G0 X0 Y5", "G1 X10 Y5 F1000" }; // cut crosses the zone (y=5)
        var issue = ToolpathPreflight.KeepOutZoneViolation("Profile 1", new[] { zone }, gcode);
        Assert.NotNull(issue);
        Assert.Equal("KEEP-OUT", issue!.RuleId);
        Assert.Contains("Clamp", issue.Message);

        // Rapid-only path is exempt.
        var rapidOnly = new List<string> { "G0 X0 Y5", "G0 X10 Y5" };
        Assert.Null(ToolpathPreflight.KeepOutZoneViolation("Profile 1", new[] { zone }, rapidOnly));
    }

    [Fact]
    public void R017_Thickness_Drift_Warns()
    {
        var issue = MachineStartPreflight.ThicknessDrift(jobThicknessMm: 18, measuredThicknessMm: 19.5);
        Assert.NotNull(issue);
        Assert.Equal("R017", issue!.RuleId);
        Assert.Equal(ToolpathPreflightFix.FixKind.UseMeasuredValue, issue.Fix.Kind);

        Assert.Null(MachineStartPreflight.ThicknessDrift(18, 18.1)); // within 0.25 tolerance
        Assert.Null(MachineStartPreflight.ThicknessDrift(18, null)); // no measurement
    }
}
