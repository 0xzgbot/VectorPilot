using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// E4: whole-job time estimate. Per-toolpath estimates already existed; nothing
/// aggregated them, split cutting from rapid travel, or costed tool changes.
/// </summary>
public class JobTimeEstimateTests
{
    private static Toolpath Tp(string name, Guid tool, params string[] gcode)
    {
        var tp = new Toolpath { Name = name, ToolId = tool };
        if (gcode.Length > 0) tp.SetResult(gcode);
        return tp;
    }

    [Fact]
    public void Empty_Job_Is_Zero()
    {
        var e = JobTimeEstimator.Estimate(Array.Empty<Toolpath>());
        Assert.Equal(0, e.TotalSeconds, 6);
        Assert.Empty(e.Toolpaths);
        Assert.Equal(0, e.ToolChanges);
    }

    [Fact]
    public void Cut_Move_Uses_The_Feed_Rate()
    {
        // 100mm at F1000 mm/min = 6s.
        var tp = Tp("cut", Guid.NewGuid(), "G0 X0 Y0", "G1 X100 Y0 F1000");
        var e = JobTimeEstimator.Estimate(new[] { tp });

        Assert.Equal(6.0, e.CuttingSeconds, 3);
        Assert.Equal(0, e.RapidSeconds, 3);
    }

    [Fact]
    public void Rapid_Move_Uses_The_Rapid_Rate()
    {
        // 100mm at 5000 mm/min = 1.2s, counted as travel not cutting.
        var tp = Tp("rapid", Guid.NewGuid(), "G0 X0 Y0", "G0 X100 Y0");
        var e = JobTimeEstimator.Estimate(new[] { tp }, rapidFeedMmPerMin: 5000);

        Assert.Equal(1.2, e.RapidSeconds, 3);
        Assert.Equal(0, e.CuttingSeconds, 3);
    }

    [Fact]
    public void Cutting_Plus_Rapid_Sums_To_The_Total()
    {
        var tp = Tp("mixed", Guid.NewGuid(),
            "G0 X0 Y0", "G1 X100 Y0 F1000", "G0 X0 Y0", "G1 X0 Y50 F1000");
        var e = JobTimeEstimator.Estimate(new[] { tp }, toolChangeSeconds: 0);

        Assert.True(e.CuttingSeconds > 0);
        Assert.True(e.RapidSeconds > 0);
        Assert.Equal(e.CuttingSeconds + e.RapidSeconds, e.TotalSeconds, 6);
    }

    [Fact]
    public void Two_Toolpaths_Sum()
    {
        var tool = Guid.NewGuid();
        var a = Tp("a", tool, "G0 X0 Y0", "G1 X60 Y0 F1000");   // 3.6s
        var b = Tp("b", tool, "G0 X0 Y0", "G1 X60 Y0 F1000");   // 3.6s

        var e = JobTimeEstimator.Estimate(new[] { a, b });
        Assert.Equal(7.2, e.CuttingSeconds, 3);
        Assert.Equal(2, e.Toolpaths.Count);
    }

    [Fact]
    public void A_Tool_Change_Adds_Exactly_The_Overhead()
    {
        var a = Tp("a", Guid.NewGuid(), "G0 X0 Y0", "G1 X60 Y0 F1000");
        var b = Tp("b", Guid.NewGuid(), "G0 X0 Y0", "G1 X60 Y0 F1000");

        var e = JobTimeEstimator.Estimate(new[] { a, b }, toolChangeSeconds: 45);

        Assert.Equal(1, e.ToolChanges);
        Assert.Equal(45, e.ToolChangeSeconds, 6);
        Assert.Equal(e.CuttingSeconds + e.RapidSeconds + 45, e.TotalSeconds, 6);
    }

    [Fact]
    public void Same_Tool_Costs_No_Change()
    {
        var tool = Guid.NewGuid();
        var e = JobTimeEstimator.Estimate(new[]
        {
            Tp("a", tool, "G1 X10 Y0 F1000"),
            Tp("b", tool, "G1 X20 Y0 F1000")
        });

        Assert.Equal(0, e.ToolChanges);
        Assert.Equal(0, e.ToolChangeSeconds, 6);
    }

    [Fact]
    public void Falls_Back_To_The_Engine_Estimate_Without_Gcode()
    {
        var tp = new Toolpath { Name = "uncalculated", EstimatedTimeSeconds = 123 };
        var e = JobTimeEstimator.Estimate(new[] { tp });
        Assert.Equal(123, e.TotalSeconds, 6);
    }

    [Fact]
    public void Comments_And_Setup_Lines_Are_Ignored()
    {
        var tp = Tp("commented", Guid.NewGuid(),
            "(VectorPilot)", "G90", "M3 S12000", "G0 X0 Y0", "G1 X60 Y0 F1000 ; raster", "M5", "M30");

        var e = JobTimeEstimator.Estimate(new[] { tp });
        Assert.Equal(3.6, e.CuttingSeconds, 3);
    }

    [Fact]
    public void Formats_As_Hours_Minutes_Seconds()
    {
        Assert.Equal("45s", new JobTimeEstimate { CuttingSeconds = 45 }.Formatted);
        Assert.Equal("8m 30s", new JobTimeEstimate { CuttingSeconds = 510 }.Formatted);
        Assert.Equal("1h 24m", new JobTimeEstimate { CuttingSeconds = 5040 }.Formatted);
    }

    [Fact]
    public void Breakdown_Reports_Per_Toolpath_Rows()
    {
        var tool = Guid.NewGuid();
        var e = JobTimeEstimator.Estimate(new[]
        {
            Tp("profile", tool, "G0 X0 Y0", "G1 X60 Y0 F1000"),
            Tp("pocket", tool, "G0 X0 Y0", "G1 X30 Y0 F600")
        });

        Assert.Collection(e.Toolpaths,
            r => Assert.Equal("profile", r.Name),
            r => Assert.Equal("pocket", r.Name));
        Assert.All(e.Toolpaths, r => Assert.True(r.TotalSeconds > 0));
    }
}
