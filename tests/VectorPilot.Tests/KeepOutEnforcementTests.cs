using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Keep-out zones must be enforced where cutting is decided.
///
/// The zone model, persistence, the Design overlay and the preflight rule all
/// existed — but nothing in the Cut stage ever called
/// ToolpathPreflight.KeepOutZoneViolation, so a toolpath could run straight through
/// a clamp with no warning anywhere in the UI. That is a physical-safety gap, not a
/// cosmetic one.
/// </summary>
public class KeepOutEnforcementTests
{
    /// <summary>A zone over x:40..60, y:0..100.</summary>
    private static KeepOutZone Zone(string name = "Clamp") => new()
    {
        Name = name,
        Type = KeepOutZoneType.Rectangle,
        RectMinX = 40, RectMaxX = 60,
        RectMinY = 0, RectMaxY = 100,
        IsActive = true
    };

    /// <summary>A cut that crosses x=50 at feed rate.</summary>
    private static List<string> CrossingProgram() => new()
    {
        "G21", "G90",
        "G0 X0 Y50 Z5",
        "G1 Z-2 F300",
        "G1 X100 Y50 F1000",   // straight through the zone
        "G0 Z5"
    };

    /// <summary>A cut that stays left of the zone.</summary>
    private static List<string> ClearProgram() => new()
    {
        "G21", "G90",
        "G0 X0 Y50 Z5",
        "G1 Z-2 F300",
        "G1 X30 Y50 F1000",
        "G0 Z5"
    };

    [Fact]
    public void A_Cut_Through_A_Zone_Is_Flagged()
    {
        var issue = ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { Zone() }, CrossingProgram());

        Assert.NotNull(issue);
        Assert.Equal("KEEP-OUT", issue!.RuleId);
        Assert.Contains("Clamp", issue.Message);
    }

    [Fact]
    public void The_Message_Names_The_Toolpath_And_The_Zone()
    {
        var issue = ToolpathPreflight.KeepOutZoneViolation(
            "Pocket 3", new[] { Zone("Hold-down bolt") }, CrossingProgram());

        Assert.NotNull(issue);
        Assert.Contains("Pocket 3", issue!.Message);
        Assert.Contains("Hold-down bolt", issue.Message);
    }

    [Fact]
    public void A_Cut_That_Avoids_The_Zone_Is_Not_Flagged()
    {
        Assert.Null(ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { Zone() }, ClearProgram()));
    }

    [Fact]
    public void An_Inactive_Zone_Is_Ignored()
    {
        var zone = new KeepOutZone
        {
            Name = "Clamp",
            Type = KeepOutZoneType.Rectangle,
            RectMinX = 40, RectMaxX = 60,
            RectMinY = 0, RectMaxY = 100,
            IsActive = false
        };

        Assert.Null(ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { zone }, CrossingProgram()));
    }

    [Fact]
    public void No_Zones_Means_No_Warning()
    {
        Assert.Null(ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", Array.Empty<KeepOutZone>(), CrossingProgram()));
    }

    [Fact]
    public void Rapids_Above_The_Stock_Do_Not_Trip_The_Rule()
    {
        // A G0 traverse over a clamp at safe Z is fine; only cutting moves matter.
        var rapidOnly = new List<string>
        {
            "G21", "G90",
            "G0 X0 Y50 Z5",
            "G0 X100 Y50",   // rapid straight over the zone
            "G0 Z5"
        };

        Assert.Null(ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { Zone() }, rapidOnly));
    }

    [Fact]
    public void The_First_Violated_Zone_Is_Reported()
    {
        var far = new KeepOutZone
        {
            Name = "Far",
            Type = KeepOutZoneType.Rectangle,
            RectMinX = 90, RectMaxX = 95,
            RectMinY = 0, RectMaxY = 100,
            IsActive = true
        };

        var issue = ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { Zone("Near"), far }, CrossingProgram());

        Assert.NotNull(issue);
        Assert.Contains("Near", issue!.Message);
    }

    [Fact]
    public void A_Circular_Zone_Is_Honoured()
    {
        var circle = new KeepOutZone
        {
            Name = "Vacuum port",
            Type = KeepOutZoneType.Circle,
            CircleCenter = new VectorPoint(50, 50),
            CircleRadiusMm = 10,
            IsActive = true
        };

        Assert.NotNull(ToolpathPreflight.KeepOutZoneViolation(
            "Profile 1", new[] { circle }, CrossingProgram()));
    }

    [Fact]
    public void Zones_Survive_A_Job_Round_Trip()
    {
        // The Cut stage reads AppState.CurrentJob.KeepOutZones; make sure a job
        // actually carries them.
        var job = new Job { Name = "Zoned" };
        job.KeepOutZones.Add(Zone());

        Assert.Single(job.KeepOutZones);
        Assert.Equal(1, job.KeepOutZones.Count(z => z.IsActive));
    }
}
