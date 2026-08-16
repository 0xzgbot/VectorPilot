using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class TextToolSignRecipeTests
{
    [Fact]
    public void TextOnCurve_Places_Glyphs_Along_Curve()
    {
        // Two glyphs along a straight line (0,0)-(100,0)
        var glyphs = new List<TextTool.GlyphOutline>
        {
            new() { Points = { new(0, 0), new(2, 0), new(2, 4), new(0, 4), new(0, 0) }, Advance = 2.5 },
            new() { Points = { new(0, 0), new(2, 0), new(2, 4), new(0, 4), new(0, 0) }, Advance = 2.5 }
        };
        var curve = new List<VectorPoint> { new(0, 0), new(100, 0) };
        var result = TextTool.TextOnCurve(glyphs, curve, 1.0, 0.5, 0.0);
        Assert.Equal(2, result.Count);
        // Both glyphs should be near the line y=0
        foreach (var shape in result)
        {
            Assert.All(shape.Points, p => Assert.InRange(p.Y, -5, 5));
        }
    }

    [Fact]
    public void TextOnCurve_Empty_Glyphs_Returns_Empty()
    {
        var curve = new List<VectorPoint> { new(0, 0), new(100, 0) };
        Assert.Empty(TextTool.TextOnCurve(new List<TextTool.GlyphOutline>(), curve));
    }

    [Fact]
    public void SignRecipe_Creates_Job_With_Text_And_Border_Layers()
    {
        var job = SignRecipeManager.CreateSignJob(text: "TEST");
        Assert.Equal("Sign Job", job.Name);
        Assert.Single(job.Sheets);
        var sheet = job.Sheets[0];
        Assert.Equal(2, sheet.Layers.Count);
        Assert.Equal("Text", sheet.Layers[0].Name);
        Assert.Equal("Border", sheet.Layers[1].Name);
        Assert.Equal(4, sheet.Layers[0].Shapes.Count); // 4 glyphs for "TEST"
        Assert.Single(sheet.Layers[1].Shapes); // 1 border
    }

    [Fact]
    public void SignRecipe_Precomputes_VCarve_For_Text()
    {
        var job = SignRecipeManager.CreateSignJob(text: "HI", vBitAngle: 90, vCarveDepth: 0.5);
        Assert.True(job.VcarvePasses > 0);
        Assert.True(job.VcarveTimeSeconds > 0);
        Assert.NotNull(job.VcarveGCode);
        Assert.True(job.VcarveGCode.Count > 3);
        Assert.Contains(job.VcarveGCode, l => l.Contains("V_CARVE"));
    }

    [Fact]
    public void SignRecipe_Border_Is_In_Stock_Coordinates()
    {
        var job = SignRecipeManager.CreateSignJob();
        var border = job.Sheets[0].Layers[1].Shapes[0];
        // Border should be centered around (228.6, 304.8) — stock center
        double cx = border.Points.Average(p => p.X);
        double cy = border.Points.Average(p => p.Y);
        Assert.InRange(cx, 200, 260);
        Assert.InRange(cy, 280, 330);
    }

    [Fact]
    public void ArcPoints_Generates_Circular_Arc()
    {
        var pts = SignRecipeManager.ArcPoints(new VectorPoint(0, 0), 100, 0, Math.PI, 10);
        Assert.Equal(11, pts.Count);
        // First point at angle 0: (100, 0)
        Assert.Equal(100, pts[0].X, 6);
        Assert.Equal(0, pts[0].Y, 6);
        // Last point at angle PI: (-100, 0)
        Assert.Equal(-100, pts[^1].X, 6);
    }
}
