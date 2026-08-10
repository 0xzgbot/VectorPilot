using VectorPilot.Engine;
using VectorPilot.Geometry;
using VectorPilot.Engine.IO;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0308 parity: keep-out zones end to end (geometry → rule → persist).</summary>
public class KeepOutZoneE2ETests
{
    private static KeepOutZone RectZone(double minX, double minY, double maxX, double maxY, bool active = true) => new()
    {
        Name = "Rect",
        Type = KeepOutZoneType.Rectangle,
        RectMinX = minX, RectMinY = minY, RectMaxX = maxX, RectMaxY = maxY,
        IsActive = active
    };

    // ---- 1. GEOMETRY ----

    [Fact]
    public void ContainsPoint_Rectangle_And_Inactive_Ignored()
    {
        var zone = RectZone(10, 10, 20, 20);
        Assert.True(zone.ContainsPoint(new VectorPoint(15, 15)));
        Assert.False(zone.ContainsPoint(new VectorPoint(5, 5)));

        zone.IsActive = false;
        Assert.False(zone.ContainsPoint(new VectorPoint(15, 15))); // inactive ignored
    }

    [Fact]
    public void ContainsPoint_Circle_And_Polygon_RayCast()
    {
        var circle = new KeepOutZone { Name = "C", Type = KeepOutZoneType.Circle, CircleCenter = new VectorPoint(0, 0), CircleRadiusMm = 5 };
        Assert.True(circle.ContainsPoint(new VectorPoint(3, 4)));
        Assert.False(circle.ContainsPoint(new VectorPoint(6, 0)));

        var poly = new KeepOutZone
        {
            Name = "P", Type = KeepOutZoneType.Polygon,
            PolygonPoints = new List<VectorPoint> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }
        };
        Assert.True(poly.ContainsPoint(new VectorPoint(5, 5)));
        Assert.False(poly.ContainsPoint(new VectorPoint(15, 5)));
    }

    [Fact]
    public void IntersectsLine_Rect_Crossing_Vs_Miss()
    {
        var zone = RectZone(10, 10, 20, 20);
        Assert.True(zone.IntersectsLine(new VectorPoint(0, 15), new VectorPoint(30, 15))); // crosses
        Assert.False(zone.IntersectsLine(new VectorPoint(0, 0), new VectorPoint(5, 5)));   // misses
        Assert.False(zone.IntersectsLine(new VectorPoint(0, 15), new VectorPoint(9, 15))); // stops short
    }

    [Fact]
    public void Manager_Aggregates_Active_Zones()
    {
        var manager = new KeepOutZoneManager();
        manager.AddZone(RectZone(10, 10, 20, 20));
        manager.AddZone(RectZone(30, 30, 40, 40, active: false));
        Assert.True(manager.ContainsPoint(new VectorPoint(15, 15)));
        Assert.False(manager.ContainsPoint(new VectorPoint(35, 35))); // inactive zone not aggregated
        Assert.True(manager.IntersectsLine(new VectorPoint(0, 15), new VectorPoint(25, 15)));
        Assert.Equal(2, manager.Zones.Count);
    }

    // ---- 2. RULE ----

    [Fact]
    public void Rule_Cut_Entering_Active_Zone_Warns_Naming_Zone()
    {
        var zone = RectZone(10, 10, 20, 20);
        var issue = ToolpathPreflight.KeepOutZoneViolation("Profile 1", new[] { zone }, new[]
        {
            "G0 X0 Y0",
            "G1 X15 Y15 F1000" // enters the zone
        });
        Assert.NotNull(issue);
        Assert.Equal(ToolpathPreflightSeverity.Warning, issue.Severity);
        Assert.Contains("Rect", issue.Message);
    }

    [Fact]
    public void Rule_G0_Rapid_Crossing_Is_Exempt()
    {
        var zone = RectZone(10, 10, 20, 20);
        var issue = ToolpathPreflight.KeepOutZoneViolation("Profile 1", new[] { zone }, new[]
        {
            "G0 X0 Y0",
            "G0 X30 Y30" // rapid-only crossing — exempt
        });
        Assert.Null(issue);
    }

    [Fact]
    public void Rule_Inactive_And_Empty_Zones_Produce_Nothing()
    {
        var inactive = RectZone(10, 10, 20, 20, active: false);
        Assert.Null(ToolpathPreflight.KeepOutZoneViolation("P", new[] { inactive }, new[] { "G1 X15 Y15" }));
        Assert.Null(ToolpathPreflight.KeepOutZoneViolation("P", new List<KeepOutZone>(), new[] { "G1 X15 Y15" }));
    }

    // ---- 3. TREE-LEVEL: per-node check flags only the offender ----

    [Fact]
    public void Tree_Level_Flags_Only_Offending_Node()
    {
        var zone = RectZone(10, 10, 20, 20);
        var clearNode = new Toolpath { Name = "Clear", Strategy = ToolpathStrategy.Profile, IsDirty = false };
        clearNode.GCode.AddRange(new[] { "G0 X0 Y0", "G1 X5 Y5 F1000" });
        var offender = new Toolpath { Name = "Offender", Strategy = ToolpathStrategy.Profile, IsDirty = false };
        offender.GCode.AddRange(new[] { "G0 X0 Y0", "G1 X15 Y15 F1000" });

        var clearIssue = ToolpathPreflight.KeepOutZoneViolation(clearNode.Name, new[] { zone }, clearNode.GCode);
        var offenderIssue = ToolpathPreflight.KeepOutZoneViolation(offender.Name, new[] { zone }, offender.GCode);
        Assert.Null(clearIssue);
        Assert.NotNull(offenderIssue);
    }

    // ---- 4. PERSIST: round-trip + legacy-safe decode ----

    [Fact]
    public void Job_Keeps_Zones_Through_Manifest_Round_Trip()
    {
        var job = Job.CreateDefault();
        job.KeepOutZones.Add(RectZone(10, 10, 20, 20));
        job.KeepOutZones.Add(new KeepOutZone { Name = "Hole", Type = KeepOutZoneType.Circle, CircleCenter = new VectorPoint(5, 5), CircleRadiusMm = 3 });

        var manifest = DocumentJson.ToManifest(job);
        var back = DocumentJson.FromManifest(manifest);

        Assert.Equal(2, back.KeepOutZones.Count);
        Assert.Equal(KeepOutZoneType.Rectangle, back.KeepOutZones[0].Type);
        Assert.Equal(10, back.KeepOutZones[0].RectMinX);
        Assert.Equal(KeepOutZoneType.Circle, back.KeepOutZones[1].Type);
        Assert.Equal(3, back.KeepOutZones[1].CircleRadiusMm);
        Assert.Equal(job.KeepOutZones[0].Id, back.KeepOutZones[0].Id); // Id preserved
    }

    [Fact]
    public void Legacy_Manifest_Without_Zones_Decodes_Null_Safe()
    {
        var legacy = "{\"id\":\"abc\",\"name\":\"Old\",\"sheetCount\":1}";
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ShopPilotManifest>(legacy, DocumentJson.Options)!;
        Assert.Null(manifest.KeepOutZones); // legacy-safe

        var job = DocumentJson.FromManifest(manifest);
        Assert.Empty(job.KeepOutZones);
    }
}
