using System.Globalization;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Holding tabs on the profile a user actually calculates.
///
/// ProfileParams.TabCount and HoldingTabGenerator both worked, and the registry's profile
/// entry honoured them — but tabCount defaulted to 0 with NO control in Cut. So a user could
/// not ask for tabs at all, and every profiled part came loose on the final pass and got
/// thrown by the cutter.
/// </summary>
public class HoldingTabsReachableTests
{
    private static readonly StrategyRegistry Reg = new();

    private static VectorShape Part() => VectorShape.Rectangle(0, 0, 100, 60);

    /// <summary>The params JSON the panel builds, via the same MergeParam it uses.</summary>
    private static string Params(int tabs) => CutPanel.MergeParam(
        """{"cutMode":2,"feedRateMmPerMin":1000,"plungeFeedRateMmPerMin":300,"maxDepthOfCutMm":2,"toolDiameterMm":6,"cutDepthMm":6,"tabLengthMm":6,"tabHeightMm":1.5}""",
        "tabCount", tabs);

    private static List<string> Profile(int tabs)
    {
        var entry = Reg.Find("profile")!;
        return entry.Compute(new[] { Part() }, null, Params(tabs)).Gcode;
    }

    /// <summary>Every distinct Z the program cuts at.</summary>
    private static List<double> CutDepths(IEnumerable<string> gcode)
    {
        var zs = new List<double>();
        foreach (var line in gcode)
        {
            var s = line.TrimStart();
            if (!s.StartsWith("G1")) continue;
            foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (tok.Length > 1 && char.ToUpperInvariant(tok[0]) == 'Z'
                    && double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    zs.Add(z);
        }
        return zs;
    }

    // ---- tabs=0 vs tabs=4 differ ----

    [Fact]
    public void Tabs_Change_The_Program()
    {
        var none = Profile(0);
        var four = Profile(4);

        Assert.NotEqual(string.Join("\n", none), string.Join("\n", four));
    }

    [Fact]
    public void Tabs_Add_Lines_Rather_Than_Removing_Them()
    {
        // Each tab lifts and re-plunges, so the program gets longer, not shorter.
        Assert.True(Profile(4).Count > Profile(0).Count,
            $"tabs=4 gave {Profile(4).Count} lines vs {Profile(0).Count} with none");
    }

    [Fact]
    public void More_Tabs_Means_More_Lifts()
    {
        Assert.True(Profile(8).Count > Profile(4).Count);
    }

    [Fact]
    public void Tabs_Introduce_A_Shallower_Z_Than_The_Full_Depth()
    {
        // A tab is uncut material: the cutter must rise above the floor and come back down.
        var withTabs = CutDepths(Profile(4));
        var without = CutDepths(Profile(0));

        Assert.NotEmpty(withTabs);

        double floorWith = withTabs.Min();
        double floorWithout = without.Min();

        // Same floor overall...
        Assert.Equal(floorWithout, floorWith, 3);

        // ...but tabs add cutting moves at a height ABOVE that floor.
        Assert.Contains(withTabs, z => z > floorWith + 0.5);
    }

    [Fact]
    public void Zero_Tabs_Cuts_Straight_Through()
    {
        var depths = CutDepths(Profile(0)).Distinct().OrderBy(z => z).ToList();
        Assert.NotEmpty(depths);
    }

    [Fact]
    public void The_Program_Still_Returns_To_A_Safe_Height()
    {
        Assert.Contains(Profile(4), l => l.TrimStart().StartsWith("G0"));
    }

    [Fact]
    public void No_NaN_With_Tabs()
    {
        Assert.DoesNotContain(Profile(4), l => l.Contains("NaN"));
    }

    // ---- the UI path uses the same params JSON ----

    [Fact]
    public void MergeParam_Sets_TabCount_Without_Losing_The_Rest()
    {
        var json = Params(4);

        Assert.Contains("\"tabCount\":4", json);
        Assert.Contains("\"toolDiameterMm\":6", json);
        Assert.Contains("\"cutDepthMm\":6", json);
    }

    [Fact]
    public void MergeParam_Overwrites_An_Existing_TabCount()
    {
        var once = CutPanel.MergeParam("""{"tabCount":2,"cutDepthMm":6}""", "tabCount", 7);

        Assert.Contains("\"tabCount\":7", once);
        Assert.DoesNotContain("\"tabCount\":2", once);
    }

    [Fact]
    public void MergeParam_Survives_Malformed_Json()
    {
        // Never throw into a Calculate: fall back to a fresh object.
        var json = CutPanel.MergeParam("not json at all", "tabCount", 3);
        Assert.Contains("\"tabCount\":3", json);
    }

    [Fact]
    public void The_Merged_Json_Is_What_The_Engine_Consumes()
    {
        // Same JSON string the panel stores on the toolpath and hands to Compute.
        var entry = Reg.Find("profile")!;
        var result = entry.Compute(new[] { Part() }, null, Params(4));

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, l => l.TrimStart().StartsWith("G1"));
    }

    [Fact]
    public void Only_Profile_Like_Strategies_Accept_Tabs()
    {
        // Injecting tabCount into a pocket or 3D finish would be ignored at best.
        Assert.True(StrategyKeyMap.IsProfileLike("profile"));
        Assert.False(StrategyKeyMap.IsProfileLike("pocket"));
        Assert.False(StrategyKeyMap.IsProfileLike("finish3d"));
        Assert.False(StrategyKeyMap.IsProfileLike(null));
    }

    // ---- the tab generator itself ----

    [Fact]
    public void Tabs_Are_Distributed_Around_The_Contour()
    {
        // Real signature: Distribute(pts, closed, count, tabLength, tabHeight) — it takes
        // the POINTS, not a precomputed length.
        var pts = Part().Points;
        double length = HoldingTabGenerator.ContourLength(pts, closed: true);

        var tabs = HoldingTabGenerator.Distribute(pts, closed: true, count: 4,
                                                  tabLength: 6.0, tabHeight: 1.0);

        Assert.Equal(4, tabs.Count);
        Assert.All(tabs, t => Assert.InRange(t.Position, 0, length));
    }

    [Fact]
    public void A_Tab_Raises_The_Cut_Depth_Where_It_Sits()
    {
        var pts = Part().Points;
        var tabs = HoldingTabGenerator.Distribute(pts, closed: true, count: 4,
                                                  tabLength: 6.0, tabHeight: 1.5);
        var first = tabs[0];

        double inTab = HoldingTabGenerator.DepthAt(first.Position, -6.0, tabs);
        double outside = HoldingTabGenerator.DepthAt(first.Position + 40, -6.0, tabs);

        Assert.True(inTab > outside, $"tab depth {inTab} should be above floor {outside}");
    }

    [Fact]
    public void Zero_Tabs_Distributes_Nothing()
    {
        Assert.Empty(HoldingTabGenerator.Distribute(Part().Points, closed: true, count: 0));
    }
}
