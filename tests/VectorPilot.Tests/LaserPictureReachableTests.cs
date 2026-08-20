using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Laser Picture in the Cut combo.
///
/// LaserCutEngine and LaserFillEngine were registered; LaserPictureEngine was not, because
/// it consumes a HEIGHTFIELD (greyscale image) instead of vectors. So raster photo-engraving
/// existed in the engine with no way to select it.
/// </summary>
public class LaserPictureReachableTests
{
    private static readonly StrategyRegistry Reg = new();

    /// <summary>A tiny 16x12 greyscale ramp — enough to raster, fast to run.</summary>
    private static HeightfieldData TinyImage(int w = 16, int h = 12)
    {
        var heights = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                heights[y * w + x] = x / (double)(w - 1) * 5.0;   // 0..5mm left to right

        return new HeightfieldData(w, h, cellSizeMm: 1.0, minX: 0, minY: 0, heights: heights);
    }

    private static List<VectorShape> Region() => new() { VectorShape.Rectangle(0, 0, 16, 12) };

    // ---- registered and selectable ----

    [Fact]
    public void The_Strategy_Is_Registered()
    {
        var entry = Reg.Find("laser-picture");
        Assert.NotNull(entry);
        Assert.Equal("Laser Picture", entry!.DisplayName);
    }

    [Fact]
    public void It_Appears_In_The_Combo_Source()
    {
        // CmbStrategy binds to Entries, so presence here is presence in the UI.
        Assert.Contains(Reg.Entries, e => e.Key == "laser-picture");
    }

    [Fact]
    public void It_Declares_That_It_Needs_A_Heightfield()
    {
        Assert.True(Reg.Find("laser-picture")!.UsesHeightfield,
            "laser-picture must declare UsesHeightfield so Cut demands a relief");
    }

    [Fact]
    public void The_Key_Maps_To_An_Enum_And_Back()
    {
        // ToString().ToLowerInvariant() would give "laserpicture", which is not the key —
        // the same trap that broke wrapped-fluting and drill-bank.
        var strategy = StrategyKeyMap.ToStrategy("laser-picture");
        Assert.Equal(ToolpathStrategy.LaserPicture, strategy);
        Assert.Equal("laser-picture", StrategyKeyMap.ToKey(strategy));
    }

    // ---- Compute with a heightfield emits real moves ----

    [Fact]
    public void A_Tiny_Heightfield_Produces_Cutting_Moves()
    {
        var entry = Reg.Find("laser-picture")!;
        var result = entry.Compute(Region(), TinyImage(), entry.DefaultsJson);

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, l =>
            l.TrimStart().StartsWith("G1") || l.TrimStart().StartsWith("G0"));
    }

    [Fact]
    public void The_Program_Carries_Laser_Power_Commands()
    {
        var entry = Reg.Find("laser-picture")!;
        var gcode = entry.Compute(Region(), TinyImage(), entry.DefaultsJson).Gcode;

        // A laser raster must modulate power (M3/M4/S) rather than just move.
        Assert.Contains(gcode, l => l.Contains('S') || l.Contains("M3") || l.Contains("M4"));
    }

    [Fact]
    public void A_Bigger_Image_Produces_More_Output()
    {
        var entry = Reg.Find("laser-picture")!;

        int small = entry.Compute(Region(), TinyImage(16, 12), entry.DefaultsJson).Gcode.Count;
        int big = entry.Compute(Region(), TinyImage(48, 36), entry.DefaultsJson).Gcode.Count;

        Assert.True(big > small, $"48x36 gave {big} lines vs {small} for 16x12");
    }

    [Fact]
    public void No_Line_Is_Malformed()
    {
        var entry = Reg.Find("laser-picture")!;
        foreach (var l in entry.Compute(Region(), TinyImage(), entry.DefaultsJson).Gcode)
            Assert.DoesNotContain("NaN", l);
    }

    // ---- a null heightfield takes the honest Empty() path ----

    [Fact]
    public void A_Null_Heightfield_Produces_No_Program()
    {
        var entry = Reg.Find("laser-picture")!;
        var result = entry.Compute(Region(), null, entry.DefaultsJson);

        Assert.Empty(result.Gcode);
    }

    [Fact]
    public void A_Null_Heightfield_Explains_Why()
    {
        var entry = Reg.Find("laser-picture")!;
        var result = entry.Compute(Region(), null, entry.DefaultsJson);

        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Contains("Model stage", result.Error!);
    }

    [Fact]
    public void A_Null_Heightfield_Does_Not_Emit_A_Runnable_Looking_Stub()
    {
        // The old Empty() returned "%" plus a comment — a valid two-line program that a
        // machine would happily run as a no-op. That must never come back.
        var entry = Reg.Find("laser-picture")!;
        var gcode = entry.Compute(Region(), null, entry.DefaultsJson).Gcode;

        Assert.DoesNotContain("%", gcode);
        Assert.DoesNotContain(gcode, l => l.TrimStart().StartsWith("G1"));
    }

    [Fact]
    public void It_Behaves_Like_The_Other_Heightfield_Strategies()
    {
        // Same honest-refusal contract as rough3d/finish3d.
        foreach (var key in new[] { "laser-picture", "rough3d", "finish3d" })
        {
            var entry = Reg.Find(key)!;
            var result = entry.Compute(Region(), null, entry.DefaultsJson);

            Assert.Empty(result.Gcode);
            Assert.False(string.IsNullOrWhiteSpace(result.Error), $"{key} refused silently");
        }
    }
}
