using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Text-on-curve (Aspire parity; App-layer since it consumes the WPF
/// GlyphTypeface outlines from TextToCurves): places each character along a
/// path, rotated to the path tangent. Produces outline VectorShapes ready
/// for the toolpath engines.
/// </summary>
public static class TextOnCurve
{
    /// <summary>
    /// Place `text` along `path` starting at `startLength` along the path.
    /// Each glyph is transformed: translated to its position, rotated to the
    /// local tangent. Returns the combined outline shapes.
    /// </summary>
    public static List<VectorShape> Place(string text, IReadOnlyList<VectorPoint> path, double startLength = 0, string fontFamily = "Arial", double size = 48.0)
    {
        var result = new List<VectorShape>();
        if (string.IsNullOrEmpty(text) || path.Count < 2) return result;

        // Sample the path into segments with cumulative lengths.
        var cum = new List<double> { 0 };
        for (int i = 1; i < path.Count; i++)
        {
            cum.Add(cum[^1] + path[i - 1].DistanceTo(path[i]));
        }
        double total = cum[^1];
        if (total <= 1e-9) return result;

        var glyphs = TextToCurves.Convert(text, fontFamily, size);
        // Glyph advance widths: use the outline bounds (point units).
        var advances = glyphs.Select(g => g.Bounds().MaxX).ToList();
        double advanceSum = advances.Sum();
        if (advanceSum <= 1e-9) return result;

        // Fit: scale text so it spans the available path length from startLength.
        double available = total - startLength;
        double scale = Math.Min(1.0, available / advanceSum);

        double along = startLength;
        for (int gi = 0; gi < glyphs.Count; gi++)
        {
            var glyph = glyphs[gi];
            double advance = advances[gi] * scale;
            // Position = midpoint of the glyph along the path.
            double center = along + advance / 2;
            if (center > total) break;
            (double x, double y, double angle) = Sample(path, cum, center);
            var transformed = Transform(glyph, x, y, angle, scale);
            result.AddRange(transformed);
            along += advance;
        }
        return result;
    }

    /// <summary>Point + tangent angle at a path length.</summary>
    private static (double X, double Y, double Angle) Sample(IReadOnlyList<VectorPoint> path, List<double> cum, double length)
    {
        int i = 0;
        while (i < cum.Count - 2 && length > cum[i + 1]) i++;
        double segLen = cum[i + 1] - cum[i];
        double t = segLen > 1e-9 ? Math.Clamp((length - cum[i]) / segLen, 0, 1) : 0;
        var a = path[i];
        var b = path[i + 1];
        double x = a.X + (b.X - a.X) * t;
        double y = a.Y + (b.Y - a.Y) * t;
        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        return (x, y, angle);
    }

    private static List<VectorShape> Transform(VectorShape shape, double ox, double oy, double angle, double scale)
    {
        var pts = shape.Points.Select(p => new VectorPoint(
            ox + (p.X * Math.Cos(angle) - p.Y * Math.Sin(angle)) * scale,
            oy + (p.X * Math.Sin(angle) + p.Y * Math.Cos(angle)) * scale)).ToList();
        return new List<VectorShape> { VectorShape.Polyline(pts, shape.Closed) };
    }
}
