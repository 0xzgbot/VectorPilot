using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0900 parity: the Texture toolpath (boundary clipping, V-groove
/// depth formula, crosshatch double passes).</summary>
public class TextureToolpathTests
{
    private static VectorShape Rect10() => VectorShape.Rectangle(0, 0, 10, 10);

    private static TextureToolpathParams P(TextureToolpathParams.Pattern pattern = TextureToolpathParams.Pattern.Parallel, double spacing = 4) => new()
    {
        PatternKind = pattern,
        SpacingMm = spacing
    };

    [Fact]
    public void Parallel_Grooves_Stay_Inside_Boundary()
    {
        var r = TextureToolpathEngine.Compute(new[] { Rect10() }, P());
        Assert.Equal("O=TEXTURE_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount >= 2);
        // Every G1 move endpoint stays inside 0..10.
        foreach (var line in r.GcodeLines.Where(l => l.StartsWith("G1 X")))
        {
            var nums = line.Split(' ').Where(t => t.StartsWith('X') || t.StartsWith('Y'))
                .Select(t => double.Parse(t[1..], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.InRange(nums[0], -0.001, 10.001);
            Assert.InRange(nums[1], -0.001, 10.001);
        }
    }

    [Fact]
    public void V_Groove_Depth_Formula_Meets_At_Bottom()
    {
        // Run width = spacing = 4, 90° V-bit → depth = 4 / (2·tan(45°)) = 2.
        var r = TextureToolpathEngine.Compute(new[] { Rect10() }, new TextureToolpathParams
        {
            SpacingMm = 4,
            VBitAngleDegrees = 90
        });
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 Z-2.000 F"));
    }

    [Fact]
    public void Max_Depth_Caps_The_V_Groove()
    {
        // Un-capped depth would be 2; cap at 0.5.
        var r = TextureToolpathEngine.Compute(new[] { Rect10() }, new TextureToolpathParams
        {
            SpacingMm = 4,
            VBitAngleDegrees = 90,
            MaxDepthMm = 0.5
        });
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 Z-0.500 F"));
        Assert.DoesNotContain(r.GcodeLines, l => l.StartsWith("G1 Z-2.000 F"));
    }

    [Fact]
    public void Crosshatch_Runs_Two_Passes()
    {
        var parallel = TextureToolpathEngine.Compute(new[] { Rect10() }, P(TextureToolpathParams.Pattern.Parallel));
        var crosshatch = TextureToolpathEngine.Compute(new[] { Rect10() }, P(TextureToolpathParams.Pattern.Crosshatch));
        Assert.Equal(2 * parallel.FeatureCount, crosshatch.FeatureCount);
    }

    [Fact]
    public void Flat_Style_Uses_Flat_Depth()
    {
        var r = TextureToolpathEngine.Compute(new[] { Rect10() }, new TextureToolpathParams
        {
            SpacingMm = 4,
            Style = TextureToolpathParams.CutStyle.Flat,
            FlatDepthMm = 1.25
        });
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 Z-1.250 F"));
    }

    [Fact]
    public void Open_Path_Is_Skipped()
    {
        var open = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 10));
        var r = TextureToolpathEngine.Compute(new[] { open }, new TextureToolpathParams());
        Assert.Equal(0, r.FeatureCount);
        Assert.Contains(r.GcodeLines, l => l == "M30");
    }
}
