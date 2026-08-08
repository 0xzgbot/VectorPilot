using VectorPilot.App;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class TextToCurvesTests
{
    [Fact]
    public void Text_Produces_Outline_Shapes()
    {
        var shapes = TextToCurves.Convert("Hi", "Arial", 48);
        Assert.NotEmpty(shapes);
        Assert.All(shapes, s => Assert.Equal(ShapeType.Polyline, s.Type));
        // Letter outlines are closed loops.
        Assert.All(shapes, s => Assert.True(s.Closed));
        // Outline points sit within the em box (y flipped to baseline-up).
        var b = new BoundingBox(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach (var s in shapes)
        {
            var sb = s.Bounds();
            b = b.Union(sb);
        }
        Assert.True(b.Width > 20 && b.Width < 200, $"width {b.Width}");
        Assert.True(b.Height > 20 && b.Height < 100, $"height {b.Height}");
    }

    [Fact]
    public void Empty_Text_Returns_Empty()
    {
        Assert.Empty(TextToCurves.Convert(""));
        Assert.Empty(TextToCurves.Convert(null!));
    }

    [Fact]
    public void Larger_Size_Yields_Larger_Outlines()
    {
        var small = TextToCurves.Convert("A", "Arial", 24);
        var large = TextToCurves.Convert("A", "Arial", 96);
        Assert.NotEmpty(small);
        Assert.NotEmpty(large);
        var smallW = new BoundingBox(0, 0, 0, 0).Union(small[0].Bounds()).Width;
        var largeW = new BoundingBox(0, 0, 0, 0).Union(large[0].Bounds()).Width;
        Assert.True(largeW > smallW * 3, $"small {smallW} large {largeW}");
    }
}
