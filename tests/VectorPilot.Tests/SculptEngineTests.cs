using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class SculptEngineTests
{
    private static HeightfieldData Flat(double v = 2.0)
    {
        var h = new double[64];
        Array.Fill(h, v);
        return new HeightfieldData(8, 8, 1.0, 0, 0, h);
    }

    [Fact]
    public void Brush_Raises_Within_Radius_Only()
    {
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Brush,
            CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 2.0, Strength = 0.5, MaxDeltaMm = 2.0
        }, Flat());

        Assert.True(r.CellsAffected > 0);
        Assert.True(r.MaxHeight > 2.0);
        // Far corner untouched.
        Assert.Equal(2.0, r.Heightfield.HeightAt(0.5, 0.5)!.Value, 6);
        Assert.Equal(2.0, r.Heightfield.HeightAt(7.5, 7.5)!.Value, 6);
    }

    [Fact]
    public void Deflate_Lowers_And_Clamps_At_Zero()
    {
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Deflate,
            CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 2.0, Strength = 0.5, MaxDeltaMm = 5.0
        }, Flat());
        Assert.True(r.MinHeight <= 2.0);
        Assert.All(r.Heightfield.Heights, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Inflate_Ignores_Strength_Sign()
    {
        var neg = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Inflate, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 1.5, Strength = -0.8, MaxDeltaMm = 1.0
        }, Flat());
        Assert.True(neg.MaxHeight > 2.0);
    }

    [Fact]
    public void Flatten_Pulls_Toward_Footprint_Mean()
    {
        var h = new double[64];
        for (int i = 0; i < 64; i++) h[i] = i % 2 == 0 ? 1.0 : 5.0;
        var hf = new HeightfieldData(8, 8, 1.0, 0, 0, h);

        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Flatten, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 3.0, Strength = 1.0, MaxDeltaMm = 10.0
        }, hf);
        // Interior cells moved toward the footprint mean (≈2.714); edge cells
        // at w≈0 legitimately stay put.
        double center = r.Heightfield.HeightAt(3.5, 3.5)!.Value;
        double mid = r.Heightfield.HeightAt(4.5, 3.5)!.Value;
        Assert.True(center < 5.0 && center > 2.0, $"center {center}");
        Assert.True(mid > 1.0 && mid < 3.0, $"mid {mid}");
    }

    [Fact]
    public void Falloff_Sphere_Center_Is_One()
    {
        Assert.Equal(1.0, SculptEngine.FalloffWeight(0, BrushShape.Sphere, BrushFalloff.Constant), 6);
        Assert.Equal(0.0, SculptEngine.FalloffWeight(1, BrushShape.Sphere, BrushFalloff.Smooth), 6);
        Assert.True(SculptEngine.FalloffWeight(0.5, BrushShape.Sphere, BrushFalloff.Smooth) > 0);
    }

    [Fact]
    public void Stroke_Off_Grid_Affects_Nothing()
    {
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Brush, CenterX = 100, CenterY = 100,
            RadiusMm = 2.0, Strength = 0.5, MaxDeltaMm = 2.0
        }, Flat());
        Assert.Equal(0, r.CellsAffected);
        Assert.Equal(2.0, r.MaxHeight, 6);
    }
}
