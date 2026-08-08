using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Text → vector curves using WPF's GlyphTypeface outlines (the DirectWrite
/// equivalent of the Mac's CoreText TextRenderer). Each glyph outline is
/// flattened to a polyline VectorShape for engraving/cutting.
/// </summary>
public static class TextToCurves
{
    /// <summary>Convert text to closed outline polylines in point units (72/inch).</summary>
    public static List<VectorShape> Convert(string text, string fontFamily = "Arial", double size = 48.0, bool bold = false, bool italic = false)
    {
        var result = new List<VectorShape>();
        if (string.IsNullOrEmpty(text)) return result;

        var typeface = new Typeface(
            new FontFamily(fontFamily),
            italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        GlyphTypeface glyphTypeface;
        if (!typeface.TryGetGlyphTypeface(out glyphTypeface!))
        {
            return result;
        }

        double emSize = size;
        double originX = 0;
        double originY = size; // baseline

        foreach (char ch in text)
        {
            ushort glyph = glyphTypeface.CharacterToGlyphMap.TryGetValue(ch, out var g) ? g : (ushort)0;
            var geometry = glyphTypeface.GetGlyphOutline(glyph, emSize, 0);
            if (geometry is not null)
            {
                var flattened = geometry.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
                foreach (var figure in flattened.Figures)
                {
                    var points = new List<VectorPoint>();
                    points.Add(new VectorPoint(figure.StartPoint.X + originX, originY - figure.StartPoint.Y));
                    foreach (var seg in figure.Segments)
                    {
                        if (seg is LineSegment ls)
                        {
                            points.Add(new VectorPoint(ls.Point.X + originX, originY - ls.Point.Y));
                        }
                        else if (seg is PolyLineSegment pls)
                        {
                            foreach (var p in pls.Points)
                            {
                                points.Add(new VectorPoint(p.X + originX, originY - p.Y));
                            }
                        }
                    }
                    if (points.Count >= 2)
                    {
                        result.Add(VectorShape.Polyline(points, closed: true));
                    }
                }
            }

            // Advance by the glyph's width (em-based; fallback 0.6em per char).
            double advance = glyphTypeface.AdvanceWidths.TryGetValue(glyph, out var aw) ? aw * emSize : 0.6 * emSize;
            originX += advance;
        }

        return result;
    }
}
