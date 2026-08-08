using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class ProfileEngineTests
{
    private static VectorShape Rect(double w = 20, double h = 10) => VectorShape.Rectangle(0, 0, w, h);

    [Fact]
    public void Profile_Emits_Header_Footer_And_Passes()
    {
        var p = new ProfileToolpathParams { CutMode = ProfileCutMode.OnCut, MaxDepthOfCutMm = 2.0, SpindleRpm = 12000 };
        var r = ProfileToolpathEngine.Compute(new[] { Rect() }, p, stockHeightMm: 5.0);
        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("O=PROFILE_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l == "M3 S12000");
        Assert.Contains(r.GcodeLines, l => l == "M30");
        Assert.Equal("%", r.GcodeLines[^1]);
        Assert.Equal(3, r.PassCount); // ceil(5/2)
        Assert.True(r.EstimatedTimeSeconds > 0);
    }

    [Fact]
    public void OutCut_Offsets_Closed_Shape_Outward()
    {
        // 20x10 rect, out cut with 6mm tool → path must extend beyond the shape.
        var p = new ProfileToolpathParams { CutMode = ProfileCutMode.OutCut, ToolDiameterMm = 6.0, MaxDepthOfCutMm = 2.0 };
        var r = ProfileToolpathEngine.Compute(new[] { Rect() }, p, stockHeightMm: 2.0);
        Assert.True(r.Path.Count > 4);
        // First cut point after the lead-in should be outside the source bounds.
        Assert.Contains(r.Path, l => l.Contains("X-3.") || l.Contains("X-2."));
    }

    [Fact]
    public void InCut_Offsets_Closed_Shape_Inward()
    {
        var p = new ProfileToolpathParams { CutMode = ProfileCutMode.InCut, ToolDiameterMm = 6.0, MaxDepthOfCutMm = 2.0 };
        var r = ProfileToolpathEngine.Compute(new[] { Rect() }, p, stockHeightMm: 2.0);
        // Inward offset of a 20x10 rect by 3 → first cut point near (3,3).
        Assert.Contains(r.Path, l => l.Contains("X3.") || l.Contains("X3."));
    }

    [Fact]
    public void FromMaterial_Sets_Feeds()
    {
        var material = Material.Oak();
        var p = ProfileToolpathParams.FromMaterial(material, 6.0);
        Assert.Equal(material.MaxFeedRateMmPerMin * 0.7, p.FeedRateMmPerMin);
        Assert.Equal(material.MaxFeedRateMmPerMin * 0.3, p.PlungeFeedRateMmPerMin);
        Assert.Equal(Math.Min(material.MaxDepthOfCutMm, 6.0), p.MaxDepthOfCutMm);
        Assert.Equal(12.0, p.LeadInDistanceMm);
    }
}

public class KeepOutZoneTests
{
    [Fact]
    public void Circle_Contains_Point()
    {
        var z = new KeepOutZone { Type = KeepOutZoneType.Circle, CircleCenter = new VectorPoint(0, 0), CircleRadiusMm = 5 };
        Assert.True(z.ContainsPoint(new VectorPoint(3, 4)));
        Assert.False(z.ContainsPoint(new VectorPoint(6, 0)));
        Assert.False(z.ContainsPoint(new VectorPoint(0, 6)));
    }

    [Fact]
    public void Rectangle_Contains_And_Intersects()
    {
        var z = new KeepOutZone { Type = KeepOutZoneType.Rectangle, RectMinX = 0, RectMinY = 0, RectMaxX = 10, RectMaxY = 10 };
        Assert.True(z.ContainsPoint(new VectorPoint(5, 5)));
        Assert.False(z.ContainsPoint(new VectorPoint(11, 5)));
        Assert.True(z.IntersectsLine(new VectorPoint(-5, 5), new VectorPoint(5, 5)));
        Assert.False(z.IntersectsLine(new VectorPoint(-5, 20), new VectorPoint(5, 20)));
    }

    [Fact]
    public void Polygon_RayCast()
    {
        var z = new KeepOutZone
        {
            Type = KeepOutZoneType.Polygon,
            PolygonPoints = new List<VectorPoint>
            {
                new(0, 0), new(10, 0), new(10, 10), new(0, 10)
            }
        };
        Assert.True(z.ContainsPoint(new VectorPoint(5, 5)));
        Assert.False(z.ContainsPoint(new VectorPoint(15, 5)));
    }

    [Fact]
    public void Inactive_Zone_Is_Ignored()
    {
        var z = new KeepOutZone { Type = KeepOutZoneType.Circle, CircleCenter = new VectorPoint(0, 0), CircleRadiusMm = 100, IsActive = false };
        Assert.False(z.ContainsPoint(new VectorPoint(1, 1)));
        var mgr = new KeepOutZoneManager();
        mgr.AddZone(z);
        Assert.Empty(mgr.ActiveZones);
        Assert.False(mgr.ContainsPoint(new VectorPoint(1, 1)));
        Assert.True(mgr.RemoveZone(z.Id));
        Assert.False(mgr.RemoveZone(z.Id));
    }
}
