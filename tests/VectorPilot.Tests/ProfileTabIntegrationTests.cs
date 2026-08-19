using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Tabs must reach the emitted G-code, on the FINAL pass only. Raising the cutter
/// on an earlier pass would leave uncut stock behind instead of a holding tab.
/// </summary>
public class ProfileTabIntegrationTests
{
    private static VectorShape Square100()
    {
        var s = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        s.Points.AddRange(new[]
        {
            new VectorPoint(0, 0), new VectorPoint(100, 0),
            new VectorPoint(100, 100), new VectorPoint(0, 100)
        });
        return s;
    }

    private static ProfileToolpathParams Params(int tabCount) => new()
    {
        CutMode = ProfileCutMode.OutCut,
        MaxDepthOfCutMm = 6.0,
        ToolDiameterMm = 6.0,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        TabCount = tabCount,
        TabLengthMm = 8.0,
        TabHeightMm = 1.5
    };

    [Fact]
    public void Without_Tabs_No_Tab_Moves_Are_Emitted()
    {
        var r = ProfileToolpathEngine.Compute(new[] { Square100() }, Params(0), stockHeightMm: 18);
        Assert.DoesNotContain(r.GcodeLines, l => l.Contains("; tab"));
    }

    [Fact]
    public void With_Tabs_Tab_Moves_Appear()
    {
        var r = ProfileToolpathEngine.Compute(new[] { Square100() }, Params(4), stockHeightMm: 18);
        Assert.Contains(r.GcodeLines, l => l.Contains("; tab"));
    }

    [Fact]
    public void Tab_Moves_Are_Shallower_Than_The_Final_Depth()
    {
        var r = ProfileToolpathEngine.Compute(new[] { Square100() }, Params(4), stockHeightMm: 18);

        var tabLines = r.GcodeLines.Where(l => l.Contains("; tab")).ToList();
        Assert.NotEmpty(tabLines);

        foreach (var line in tabLines)
        {
            var zTok = line.Split(' ').First(t => t.StartsWith("Z"));
            double z = double.Parse(zTok[1..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(z > -18.0, $"tab Z {z} must be shallower than the -18 cut depth");
        }
    }

    [Fact]
    public void Tabs_Only_Appear_On_The_Final_Pass()
    {
        // 18mm at 6mm per pass = 3 passes; only the last should carry tabs.
        var r = ProfileToolpathEngine.Compute(new[] { Square100() }, Params(4), stockHeightMm: 18);

        int lastPassStart = r.GcodeLines.FindLastIndex(l => l.Contains("(Pass "));
        Assert.True(lastPassStart > 0);

        for (int i = 0; i < lastPassStart; i++)
            Assert.DoesNotContain("; tab", r.GcodeLines[i]);

        Assert.Contains(r.GcodeLines.Skip(lastPassStart), l => l.Contains("; tab"));
    }

    [Fact]
    public void HasTabs_Reflects_The_Tab_Count()
    {
        Assert.True(ProfileToolpathEngine.Compute(new[] { Square100() }, Params(3), 18).HasTabs);
        Assert.False(ProfileToolpathEngine.Compute(new[] { Square100() }, Params(0), 18).HasTabs);
    }
}
