using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Real glyph extraction (WPF GlyphTypeface) — proves sign text renders as
/// actual letterforms, not the engine's placeholder boxes.
/// </summary>
public class GlyphExtractorTests
{
    [Fact]
    public void System_Fonts_Are_Discoverable()
    {
        var fonts = GlyphExtractor.AvailableFonts();
        Assert.NotEmpty(fonts);
        Assert.Contains(fonts, f => f.Contains("Segoe", StringComparison.OrdinalIgnoreCase)
                                 || f.Contains("Arial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_Returns_One_Outline_Per_Character()
    {
        var glyphs = GlyphExtractor.Extract("ABC", "Segoe UI", 72);
        Assert.Equal(3, glyphs.Count);
        Assert.All(glyphs, g => Assert.True(g.Advance > 0));
    }

    [Fact]
    public void Real_Glyphs_Are_Not_Placeholder_Boxes()
    {
        // The engine placeholder was a 5-point 2x4 rectangle. A real 'B' outline
        // has far more points and a curved profile.
        var glyphs = GlyphExtractor.Extract("B", "Segoe UI", 72);
        Assert.Single(glyphs);
        Assert.True(glyphs[0].Points.Count > 20,
            $"expected a real outline, got {glyphs[0].Points.Count} points");
    }

    [Fact]
    public void Different_Letters_Produce_Different_Outlines()
    {
        var i = GlyphExtractor.Extract("I", "Segoe UI", 72)[0];
        var o = GlyphExtractor.Extract("O", "Segoe UI", 72)[0];
        // 'O' is a curved closed ring; 'I' is a simple stem — point counts differ.
        Assert.NotEqual(i.Points.Count, o.Points.Count);
    }

    [Fact]
    public void Advance_Scales_With_Font_Size()
    {
        var small = GlyphExtractor.Extract("W", "Segoe UI", 20)[0];
        var large = GlyphExtractor.Extract("W", "Segoe UI", 80)[0];
        Assert.True(large.Advance > small.Advance * 3.5);
    }

    [Fact]
    public void Whitespace_Advances_Without_Geometry()
    {
        var glyphs = GlyphExtractor.Extract("A B", "Segoe UI", 72);
        Assert.Equal(3, glyphs.Count);
        Assert.Empty(glyphs[1].Points);      // the space has no outline
        Assert.True(glyphs[1].Advance > 0);  // but still advances the pen
    }

    [Fact]
    public void Unknown_Font_Falls_Back_Instead_Of_Throwing()
    {
        var glyphs = GlyphExtractor.Extract("A", "ThisFontDoesNotExist12345", 72);
        Assert.Single(glyphs);
        Assert.True(glyphs[0].Points.Count > 3);
    }

    [Fact]
    public void Empty_Text_Returns_Empty()
    {
        Assert.Empty(GlyphExtractor.Extract("", "Segoe UI", 72));
    }

    [Fact]
    public void Extracted_Glyphs_Drive_The_Sign_Recipe()
    {
        var glyphs = GlyphExtractor.Extract("HI", "Segoe UI", 48);
        var job = SignRecipeManager.CreateSignJob(text: "HI", glyphs: glyphs);

        var textLayer = job.Sheets[^1].Layers.First(l => l.Name == "Text");
        Assert.Equal(2, textLayer.Shapes.Count);
        // Real letterforms, not 5-point boxes: 'H' is a multi-segment outline.
        // ('I' is a simple stem, so assert on the richest glyph, not every one.)
        Assert.True(textLayer.Shapes.Max(s => s.Points.Count) > 10,
            $"expected real outlines, max was {textLayer.Shapes.Max(s => s.Points.Count)}");
        Assert.All(textLayer.Shapes, s => Assert.True(s.Points.Count >= 5));
        Assert.True(job.VcarvePasses > 0);
    }

    [Fact]
    public void Glyphs_Are_In_Math_Coordinates_Y_Up()
    {
        // WPF text geometry is Y-down; the extractor flips it. A capital letter
        // therefore extends upward (positive Y) from the baseline at 0.
        var glyph = GlyphExtractor.Extract("T", "Segoe UI", 72)[0];
        Assert.True(glyph.Points.Max(p => p.Y) > 0);
    }
}
