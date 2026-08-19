using System.Windows;
using System.Windows.Media;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Real glyph outline extraction via WPF GlyphTypeface (replaces the engine's
/// placeholder box glyphs). Produces TextTool.GlyphOutline lists that the
/// engine's text-on-curve placement consumes, so sign text renders as actual
/// letterforms instead of rectangles.
/// </summary>
public static class GlyphExtractor
{
    /// <summary>Fonts installed on this machine, sorted by name.</summary>
    public static List<string> AvailableFonts()
        => Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Extract per-character outlines for a string. Y is flipped so the result
    /// is in CNC math coordinates (Y up).
    /// </summary>
    public static List<TextTool.GlyphOutline> Extract(
        string text,
        string fontName = "Segoe UI",
        double fontSize = 72,
        double flatnessTolerance = 0.1)
    {
        var result = new List<TextTool.GlyphOutline>();
        if (string.IsNullOrEmpty(text)) return result;

        var glyphTypeface = ResolveTypeface(fontName);
        if (glyphTypeface is null) return result;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                result.Add(new TextTool.GlyphOutline { Advance = fontSize * 0.28 });
                continue;
            }

            if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(c, out ushort glyphIndex))
            {
                result.Add(new TextTool.GlyphOutline { Advance = fontSize * 0.28 });
                continue;
            }

            double advance = glyphTypeface.AdvanceWidths[glyphIndex] * fontSize;
            var geometry = glyphTypeface.GetGlyphOutline(glyphIndex, fontSize, fontSize);
            var points = Flatten(geometry, flatnessTolerance);

            result.Add(new TextTool.GlyphOutline { Points = points, Advance = advance });
        }

        return result;
    }

    /// <summary>True when the named font resolves to a real glyph typeface.</summary>
    public static bool FontIsAvailable(string fontName) => ResolveTypeface(fontName) is not null;

    private static GlyphTypeface? ResolveTypeface(string fontName)
    {
        var typeface = new Typeface(new FontFamily(fontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out var gt)) return gt;

        // Fall back to a font that always exists on Windows.
        var fallback = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return fallback.TryGetGlyphTypeface(out var fgt) ? fgt : null;
    }

    /// <summary>Flatten a glyph geometry to a polyline, flipping Y into math coords.</summary>
    private static List<VectorPoint> Flatten(System.Windows.Media.Geometry geometry, double tolerance)
    {
        var pts = new List<VectorPoint>();
        var flat = geometry.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);

        foreach (var figure in flat.Figures)
        {
            var start = figure.StartPoint;
            pts.Add(new VectorPoint(start.X, -start.Y));

            foreach (var seg in figure.Segments)
            {
                switch (seg)
                {
                    case PolyLineSegment poly:
                        foreach (var p in poly.Points) pts.Add(new VectorPoint(p.X, -p.Y));
                        break;
                    case LineSegment line:
                        pts.Add(new VectorPoint(line.Point.X, -line.Point.Y));
                        break;
                }
            }

            if (figure.IsClosed && pts.Count > 0)
            {
                pts.Add(new VectorPoint(start.X, -start.Y));
            }
        }

        return pts;
    }
}
