using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0907 parity: the Drag Knife toolpath (blade-offset center path
/// with corner pivots).</summary>
public class DragKnifeToolpathTests
{
    [Fact]
    public void Marker_And_Start_Offset_Geometry()
    {
        // Straight line from (0,0) to (10,0): center starts at (4,0) with a
        // 4mm blade offset AHEAD of the tip (tip lands on (0,0)).
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var r = DragKnifeToolpathEngine.Compute(new[] { line }, new DragKnifeToolpathParams { BladeOffsetMm = 4 });

        Assert.Contains(r.GcodeLines, l => l == "O=DRAG_KNIFE_TOOLPATH");

        var starts = r.GcodeLines.Where(l => l.StartsWith("G0 X")).ToList();
        Assert.NotEmpty(starts);
        // First rapid positions the center offset ahead of the travel start.
        Assert.Contains(starts, l => l.Contains("X4.000"));

        var ends = r.GcodeLines.Where(l => l.StartsWith("G1 X")).ToList();
        Assert.NotEmpty(ends);
        Assert.Contains(ends, l => l.Contains("X14.000")); // center ends beyond the tip target
    }

    [Fact]
    public void Corner_Pivots_With_A_Single_Arc()
    {
        // L-shape: (0,0) → (10,0) → (10,10). The corner pivots with one arc.
        var path = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        path.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(10, 0), new VectorPoint(10, 10) });

        var r = DragKnifeToolpathEngine.Compute(new[] { path }, new DragKnifeToolpathParams { BladeOffsetMm = 4 });
        var arcs = r.GcodeLines.Where(l => l.StartsWith("G2") || l.StartsWith("G3")).ToList();

        Assert.Single(arcs);
        Assert.StartsWith("G3", arcs[0]); // CCW turn → G3
        Assert.Contains("X10.000 Y4.000", arcs[0]); // arc ends at the pivot offset
    }

    [Fact]
    public void No_Arc_On_Straight_Path()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var r = DragKnifeToolpathEngine.Compute(new[] { line }, new DragKnifeToolpathParams { BladeOffsetMm = 4 });
        Assert.DoesNotContain(r.GcodeLines, l => l.StartsWith("G2") || l.StartsWith("G3"));
    }
}

/// <summary>SPK-0802 parity: V-Carve inlay recipe presets wired to the real
/// InlayToolpathEngine.</summary>
public class InlayRecipeTests
{
    [Fact]
    public void Four_Preset_Recipes_At_All_Angles()
    {
        var presets = InlayEngine.PresetRecipes;
        Assert.Equal(4, presets.Count);
        var angles = presets.Select(r => r.VCarveAngle).Distinct().OrderBy(a => a).ToArray();
        Assert.Equal(new[] { VCaveAngle.Angle30, VCaveAngle.Angle45, VCaveAngle.Angle60, VCaveAngle.Angle90 }, angles);
    }

    [Fact]
    public void All_Presets_Have_Positive_Depth_And_Feeds()
    {
        foreach (var preset in InlayEngine.PresetRecipes)
        {
            Assert.True(preset.MaxDepthMm > 0, $"{preset.Name} depth");
            Assert.True(preset.FeedRateMmPerMin > 0 && preset.PlungeFeedRateMmPerMin > 0, $"{preset.Name} feeds");
        }
    }

    [Fact]
    public void Named_Lookup_Finds_The_30_Degree_Preset()
    {
        var fine = InlayEngine.PresetRecipes.FirstOrDefault(r => r.Name.Contains("30"));
        Assert.NotNull(fine);
        Assert.Equal(VCaveAngle.Angle30, fine.VCarveAngle);
    }

    [Fact]
    public void Recipe_Params_Flow_Into_The_Engine()
    {
        var recipe = InlayEngine.PresetRecipes.First(r => r.VCarveAngle == VCaveAngle.Angle45);
        var result = InlayEngine.GenerateInlay(new InlayPocketParams
        {
            InlayType = InlayType.VCarve,
            VCarveAngle = recipe.VCarveAngle,
            Depth = recipe.MaxDepthMm
        });
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.ToolpathLengthMm > 0);
        Assert.True(result.EstimatedTimeMinutes > 0);
    }
}
