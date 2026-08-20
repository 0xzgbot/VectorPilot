using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Three engines shipped with no registry key, so no user could select them:
/// RotaryWrapEngine, WrappedFlutingToolpathEngine and DrillBankEngine. Registering a key
/// with no engine would be worse than leaving it out, so each is asserted to emit real
/// cutting moves through entry.Compute — the same call CutPanel makes.
/// </summary>
public class NewlyRegisteredStrategyTests
{
    private static readonly StrategyRegistry Reg = new();

    private static List<VectorShape> Path() => new()
    {
        VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(50, 0), new(100, 0), new(150, 0)
        }, closed: false)
    };

    private static List<VectorShape> Rect() => new() { VectorShape.Rectangle(0, 0, 120, 80) };

    [Theory]
    [InlineData("rotary-wrap", "Rotary Wrap")]
    [InlineData("wrapped-fluting", "Wrapped Fluting")]
    [InlineData("drill-bank", "Drill Bank")]
    public void The_Strategy_Is_Registered(string key, string displayName)
    {
        var entry = Reg.Find(key);
        Assert.NotNull(entry);
        Assert.Equal(displayName, entry!.DisplayName);
    }

    [Theory]
    [InlineData("rotary-wrap")]
    [InlineData("wrapped-fluting")]
    [InlineData("drill-bank")]
    public void The_Key_Maps_To_An_Enum_And_Back(string key)
    {
        var strategy = StrategyKeyMap.ToStrategy(key);
        Assert.Equal(key, StrategyKeyMap.ToKey(strategy));
    }

    [Theory]
    [InlineData("rotary-wrap")]
    [InlineData("wrapped-fluting")]
    [InlineData("drill-bank")]
    public void It_Emits_G1_Through_Compute(string key)
    {
        var entry = Reg.Find(key)!;
        var result = entry.Compute(Path(), null, entry.DefaultsJson);

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, l => l.TrimStart().StartsWith("G1"));
    }

    [Fact]
    public void Drill_Bank_Generates_Its_Own_Grid()
    {
        // The engine builds the grid from the params, so it needs no selection at all.
        var entry = Reg.Find("drill-bank")!;
        var result = entry.Compute(new List<VectorShape>(), null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.True(result.FeatureCount >= 6, $"default 3x2 grid produced {result.FeatureCount} holes");
    }

    [Fact]
    public void A_Bigger_Drill_Grid_Makes_More_Holes()
    {
        var entry = Reg.Find("drill-bank")!;

        int Holes(int cols, int rows) => entry
            .Compute(new List<VectorShape>(), null,
                $"{{\"gridCols\":{cols},\"gridRows\":{rows},\"cutDepthMm\":8}}")
            .FeatureCount;

        Assert.True(Holes(5, 4) > Holes(2, 2));
    }

    [Fact]
    public void Wrapped_Fluting_Refuses_A_Selection_With_Too_Few_Points()
    {
        var entry = Reg.Find("wrapped-fluting")!;
        var single = new List<VectorShape>
        {
            VectorShape.Polyline(new List<VectorPoint> { new(0, 0) }, closed: false)
        };

        var result = entry.Compute(single, null, entry.DefaultsJson);

        Assert.Empty(result.Gcode);
        Assert.Contains("at least two points", result.Error!);
    }

    [Fact]
    public void Rotary_Wrap_Uses_The_Cylinder_Diameter()
    {
        var entry = Reg.Find("rotary-wrap")!;

        // RotaryWrapParams calls it DiameterMm (WrappedFlutingParams uses
        // WrapDiameterMm) — a wrong key here would be silently ignored by the
        // deserializer and the test would pass against identical output.
        var thin = entry.Compute(Path(), null, """{"diameterMm":30,"cutDepthMm":2}""");
        var thick = entry.Compute(Path(), null, """{"diameterMm":200,"cutDepthMm":2}""");

        Assert.NotEqual(
            string.Join("\n", thin.Gcode),
            string.Join("\n", thick.Gcode));
    }

    [Fact]
    public void Wrapped_Fluting_Uses_The_Wrap_Diameter_Too()
    {
        var entry = Reg.Find("wrapped-fluting")!;

        var thin = entry.Compute(Path(), null, """{"wrapDiameterMm":30,"cutDepthMm":3}""");
        var thick = entry.Compute(Path(), null, """{"wrapDiameterMm":150,"cutDepthMm":3}""");

        Assert.NotEqual(
            string.Join("\n", thin.Gcode),
            string.Join("\n", thick.Gcode));
    }

    [Fact]
    public void All_Three_Appear_In_The_Combo_Source()
    {
        // CmbStrategy binds to Entries, so presence here is presence in the UI.
        foreach (var key in new[] { "rotary-wrap", "wrapped-fluting", "drill-bank" })
            Assert.Contains(Reg.Entries, e => e.Key == key);
    }

    [Fact]
    public void No_Registered_Key_Lacks_An_Engine()
    {
        // Every key must produce either G-code or an explicit reason — never silence.
        foreach (var entry in Reg.Entries)
        {
            var result = entry.Compute(Rect(), null, entry.DefaultsJson);
            bool ok = result.Gcode.Count > 0 || !string.IsNullOrWhiteSpace(result.Error);
            Assert.True(ok, $"{entry.Key} produced neither G-code nor an error");
        }
    }
}
