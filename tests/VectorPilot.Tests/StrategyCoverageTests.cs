using System.Text.Json;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Every registered strategy must be reachable from the Cut panel and dispatch
/// through the registry.
///
/// The bug this pins: CutPanel keyed the registry with
/// <c>strategy.ToString().ToLowerInvariant()</c>, so keys like "photo-vcarve",
/// "bevel", "inlay-pocket" and "dragknife" never resolved and Calculate silently
/// fell back to the profile-only generator. The user picked one strategy and got
/// another, with no warning.
/// </summary>
public class StrategyCoverageTests
{
    private static readonly StrategyRegistry Reg = new();

    private static List<VectorShape> Shapes()
    {
        var rect = VectorShape.Rectangle(10, 10, 60, 40);
        var open = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        open.Points.AddRange(new[]
        {
            new VectorPoint(0, 0), new VectorPoint(60, 0), new VectorPoint(60, 30)
        });
        return new List<VectorShape> { rect, open };
    }

    private static HeightfieldData Relief()
    {
        var heights = new double[24 * 24];
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                heights[y * 24 + x] = 2.0 + Math.Sin(x * 0.3) * 0.8;
        return new HeightfieldData(24, 24, 1.0, 0, 0, heights);
    }

    // ---- the map must cover the registry ----

    [Fact]
    public void Every_Registry_Key_Is_Mapped_To_An_Enum_Case()
    {
        var unmapped = Reg.Entries
            .Where(e => !StrategyKeyMap.Keys.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Key)
            .ToList();

        Assert.True(unmapped.Count == 0,
            "registry keys with no enum mapping (these silently hit the legacy profile path): "
            + string.Join(", ", unmapped));
    }

    [Fact]
    public void No_Registered_Strategy_Resolves_By_Naive_Enum_Lowercasing()
    {
        // Documents the original defect: the naive key misses these entries entirely.
        var enumNames = Enum.GetNames<ToolpathStrategy>().Select(n => n.ToLowerInvariant()).ToHashSet();
        var missedByNaiveKey = Reg.Entries.Where(e => !enumNames.Contains(e.Key)).Select(e => e.Key).ToList();

        Assert.NotEmpty(missedByNaiveKey);                 // the bug was real
        foreach (var key in missedByNaiveKey)              // and the map now covers each one
            Assert.NotNull(Reg.Find(key));
    }

    [Fact]
    public void Mapped_Keys_Round_Trip_Through_The_Registry()
    {
        foreach (var entry in Reg.Entries)
        {
            var strategy = StrategyKeyMap.ToStrategy(entry.Key);
            var backKey = StrategyKeyMap.ToKey(strategy);
            Assert.NotNull(Reg.Find(backKey));   // the fallback key always resolves
        }
    }

    // ---- every strategy computes through the registry ----

    [Theory]
    [InlineData("profile")]
    [InlineData("pocket")]
    [InlineData("vcarve")]
    [InlineData("quickengrave")]
    [InlineData("quickengrave2")]
    [InlineData("prism")]
    [InlineData("fluting")]
    [InlineData("chamfer")]
    [InlineData("bevel")]
    [InlineData("dragknife")]
    [InlineData("texture")]
    [InlineData("inlay-pocket")]
    [InlineData("inlay-plug")]
    [InlineData("laser-cut")]
    [InlineData("laser-fill")]
    [InlineData("moulding")]
    [InlineData("weave")]
    public void Vector_Strategy_Emits_Gcode_Through_Compute(string key)
    {
        var entry = Reg.Find(key);
        Assert.NotNull(entry);

        var result = entry!.Compute(Shapes(), null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Contains(result.Gcode, l => l.StartsWith("G0") || l.StartsWith("G1") || l.StartsWith("M"));
    }

    [Theory]
    [InlineData("rough3d")]
    [InlineData("finish3d")]
    [InlineData("photo-vcarve")]
    [InlineData("sketch-carve")]
    public void Heightfield_Strategy_Emits_Gcode_With_A_Relief(string key)
    {
        var entry = Reg.Find(key);
        Assert.NotNull(entry);
        Assert.True(entry!.UsesHeightfield, $"{key} should declare UsesHeightfield");

        var result = entry.Compute(Shapes(), Relief(), entry.DefaultsJson);
        Assert.NotEmpty(result.Gcode);
    }

    [Fact]
    public void Every_Entry_Has_A_Display_Name_And_Valid_Defaults()
    {
        foreach (var e in Reg.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName), $"{e.Key} has no display name");
            // DefaultsJson feeds the params form; it must parse.
            using var doc = JsonDocument.Parse(e.DefaultsJson);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Fact]
    public void Keys_Are_Unique()
    {
        var dupes = Reg.Entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "duplicate registry keys: " + string.Join(", ", dupes));
    }

    [Fact]
    public void The_Registry_Covers_More_Than_The_Old_Hardcoded_Combo()
    {
        // The combo used to list 6 items against 20+ registered strategies.
        Assert.True(Reg.Entries.Count >= 20, $"only {Reg.Entries.Count} strategies registered");
    }

    [Fact]
    public void Unknown_Keys_Do_Not_Resolve()
    {
        Assert.Null(Reg.Find("no-such-strategy"));
    }
}
