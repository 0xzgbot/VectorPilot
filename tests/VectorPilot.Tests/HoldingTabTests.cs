using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Holding tabs / bridges. A part cut fully free on the final pass can lift and be
/// thrown by the cutter — tabs are a safety feature, so the depth behaviour and the
/// refusal to place overlapping tabs both matter.
/// </summary>
public class HoldingTabTests
{
    /// <summary>A 100x100 square: perimeter 400mm.</summary>
    private static List<VectorPoint> Square() => new()
    {
        new VectorPoint(0, 0), new VectorPoint(100, 0),
        new VectorPoint(100, 100), new VectorPoint(0, 100)
    };

    [Fact]
    public void Contour_Length_Closes_The_Loop()
    {
        Assert.Equal(400, HoldingTabGenerator.ContourLength(Square(), closed: true), 6);
        Assert.Equal(300, HoldingTabGenerator.ContourLength(Square(), closed: false), 6);
    }

    [Fact]
    public void Four_Tabs_Are_Evenly_Spaced()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6);

        Assert.Equal(4, tabs.Count);
        // Spacing 100mm, tab centred in each span: 50 - 3 = 47, then +100 each.
        Assert.Equal(47, tabs[0].Position, 6);
        Assert.Equal(147, tabs[1].Position, 6);
        Assert.Equal(247, tabs[2].Position, 6);
        Assert.Equal(347, tabs[3].Position, 6);
    }

    [Fact]
    public void No_Tab_Sits_On_The_Start_Point()
    {
        // Lead-in moves land at distance 0; a tab there would be cut through.
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6);
        Assert.All(tabs, t => Assert.True(t.Position > 0));
    }

    [Fact]
    public void Overlapping_Tabs_Are_Refused()
    {
        // 400mm contour cannot hold 100 tabs of 6mm.
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 100, tabLength: 6);
        Assert.Empty(tabs);
    }

    [Fact]
    public void A_Degenerate_Contour_Gets_No_Tabs()
    {
        var single = new List<VectorPoint> { new VectorPoint(5, 5) };
        Assert.Empty(HoldingTabGenerator.Distribute(single, true, 4));
        Assert.Empty(HoldingTabGenerator.Distribute(Square(), true, count: 0));
    }

    [Fact]
    public void IsInTab_Covers_The_Whole_Tab_Span()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6);

        Assert.False(HoldingTabGenerator.IsInTab(46.9, tabs));   // just before
        Assert.True(HoldingTabGenerator.IsInTab(47.0, tabs));    // leading edge
        Assert.True(HoldingTabGenerator.IsInTab(50.0, tabs));    // middle
        Assert.True(HoldingTabGenerator.IsInTab(53.0, tabs));    // trailing edge
        Assert.False(HoldingTabGenerator.IsInTab(53.1, tabs));   // just after
    }

    [Fact]
    public void Depth_Rises_By_The_Tab_Height_Over_A_Tab()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6, tabHeight: 1.5);

        Assert.Equal(-19.0, HoldingTabGenerator.DepthAt(10, -19.0, tabs), 6);   // full depth
        Assert.Equal(-17.5, HoldingTabGenerator.DepthAt(50, -19.0, tabs), 6);   // over a tab
    }

    [Fact]
    public void A_Tab_Taller_Than_The_Cut_Never_Rises_Above_The_Surface()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6, tabHeight: 50);
        // Tab height exceeds the cut depth: clamp at the stock surface, not above it.
        Assert.Equal(0, HoldingTabGenerator.DepthAt(50, -19.0, tabs), 6);
    }

    [Fact]
    public void Without_Tabs_Depth_Is_Unchanged()
    {
        var none = new List<HoldingTab>();
        Assert.Equal(-12.0, HoldingTabGenerator.DepthAt(50, -12.0, none), 6);
        Assert.False(HoldingTabGenerator.IsInTab(50, none));
    }

    [Fact]
    public void A_Single_Tab_Is_Centred_On_The_Contour()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 1, tabLength: 10);
        Assert.Single(tabs);
        Assert.Equal(195, tabs[0].Position, 6);   // 400/2 - 5
    }

    [Fact]
    public void TabAt_Returns_The_Covering_Tab()
    {
        var tabs = HoldingTabGenerator.Distribute(Square(), true, count: 4, tabLength: 6, tabHeight: 2);
        var tab = HoldingTabGenerator.TabAt(148, tabs);

        Assert.NotNull(tab);
        Assert.Equal(2, tab!.Height, 6);
        Assert.Null(HoldingTabGenerator.TabAt(10, tabs));
    }
}
