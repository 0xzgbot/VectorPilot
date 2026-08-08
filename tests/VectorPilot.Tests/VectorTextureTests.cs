using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class VectorTextureTests
{
    [Fact]
    public void Crosshatch_Fills_Boundary()
    {
        var shapes = VectorTextureEngine.Generate(
            new[] { VectorShape.Rectangle(0, 0, 20, 20) },
            new VectorTextureEngine.Params { Pattern = VectorTextureEngine.PatternKind.Crosshatch, SpacingMm = 5 });
        Assert.True(shapes.Count >= 3);
        Assert.All(shapes, s => Assert.Equal(ShapeType.Line, s.Type));
        // Clipped: all lines are inside the 0..20 box.
        Assert.All(shapes, s =>
        {
            Assert.True(s.Points[0].X >= -1e-9 && s.Points[0].X <= 20 + 1e-9);
            Assert.True(s.Points[1].X >= -1e-9 && s.Points[1].X <= 20 + 1e-9);
        });
    }

    [Fact]
    public void Dots_Produce_Closed_Ellipses()
    {
        var shapes = VectorTextureEngine.Generate(
            new[] { VectorShape.Rectangle(0, 0, 10, 10) },
            new VectorTextureEngine.Params { Pattern = VectorTextureEngine.PatternKind.Dots, DotDiameterMm = 2, SpacingMm = 3 });
        Assert.True(shapes.Count >= 4);
        Assert.All(shapes, s => Assert.True(s.Closed));
    }

    [Fact]
    public void Zigzag_Produces_Polylines()
    {
        var shapes = VectorTextureEngine.Generate(
            new[] { VectorShape.Rectangle(0, 0, 10, 10) },
            new VectorTextureEngine.Params { Pattern = VectorTextureEngine.PatternKind.Zigzag, SpacingMm = 4 });
        Assert.True(shapes.Count >= 1);
        Assert.All(shapes, s => Assert.Equal(ShapeType.Polyline, s.Type));
    }

    [Fact]
    public void No_Clip_Keeps_Full_Lines()
    {
        var clipped = VectorTextureEngine.Generate(
            new[] { VectorShape.Rectangle(0, 0, 10, 10) },
            new VectorTextureEngine.Params { SpacingMm = 5, ClipToBoundary = true });
        var unclipped = VectorTextureEngine.Generate(
            new[] { VectorShape.Rectangle(0, 0, 10, 10) },
            new VectorTextureEngine.Params { SpacingMm = 5, ClipToBoundary = false });
        Assert.True(unclipped.Count >= clipped.Count);
    }
}
