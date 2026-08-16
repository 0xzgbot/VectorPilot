using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Text-on-curve placement (ported from TextTool.swift): pure geometry —
/// places pre-extracted glyph outlines along a curve, rotating each to follow
/// the tangent. Glyph extraction (WPF/CoreText) lives in the App layer.
/// </summary>
public static class TextTool
{
    /// <summary>A glyph outline (points) + its advance width.</summary>
    public sealed class GlyphOutline
    {
        public List<VectorPoint> Points { get; set; } = new();
        public double Advance { get; set; }
    }

    /// <summary>Place glyph outlines along a curve.</summary>
    public static List<VectorShape> TextOnCurve(
        List<GlyphOutline> glyphs,
        List<VectorPoint> curvePoints,
        double scale = 1.0,
        double offset = 0.5,
        double letterSpacing = 0.0)
    {
        if (glyphs.Count == 0 || curvePoints.Count < 2) return new List<VectorShape>();

        var samples = SampleCurve(curvePoints, 100);
        double totalLength = CurveLength(samples);
        double totalWidth = glyphs.Sum(g => g.Advance);
        double startOffset = (totalWidth / 2.0) - (totalLength * offset);

        var result = new List<VectorShape>();
        double currentDistance = 0;

        foreach (var glyph in glyphs)
        {
            double charCenter = currentDistance + startOffset + (glyph.Advance / 2.0);
            var (point, tangent) = PointAtDistance(samples, charCenter);
            double angle = Math.Atan2(tangent.Y, tangent.X);

            // Center glyph at origin, rotate, then translate
            double cx = 0, cy = 0;
            if (glyph.Points.Count > 0)
            {
                double minX = glyph.Points.Min(p => p.X), maxX = glyph.Points.Max(p => p.X);
                double minY = glyph.Points.Min(p => p.Y), maxY = glyph.Points.Max(p => p.Y);
                cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
            }

            var centered = glyph.Points.Select(p => new VectorPoint((p.X - cx) * scale, (p.Y - cy) * scale)).ToList();
            var rotated = centered.Select(p =>
            {
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                return new VectorPoint(p.X * cos - p.Y * sin + point.X, p.X * sin + p.Y * cos + point.Y);
            }).ToList();

            var shape = new VectorShape { Type = ShapeType.Polyline, Closed = true };
            shape.Points.AddRange(rotated);
            result.Add(shape);
            currentDistance += glyph.Advance + letterSpacing;
        }

        return result;
    }

    private static List<(VectorPoint Point, VectorPoint Tangent)> SampleCurve(List<VectorPoint> points, int numSamples)
    {
        var result = new List<(VectorPoint, VectorPoint)>();
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / (numSamples - 1);
            double dist = t * TotalLength(points);
            var (pt, tan) = PointAtCumulative(points, dist);
            result.Add((pt, tan));
        }
        return result;
    }

    private static double TotalLength(List<VectorPoint> points)
    {
        double len = 0;
        for (int i = 1; i < points.Count; i++) len += Distance(points[i - 1], points[i]);
        return len;
    }

    private static double CurveLength(List<(VectorPoint Point, VectorPoint Tangent)> samples)
    {
        double len = 0;
        for (int i = 1; i < samples.Count; i++) len += Distance(samples[i - 1].Point, samples[i].Point);
        return len;
    }

    private static (VectorPoint, VectorPoint) PointAtCumulative(List<VectorPoint> points, double targetDist)
    {
        double accumulated = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double segLen = Distance(points[i - 1], points[i]);
            if (accumulated + segLen >= targetDist)
            {
                double t = (targetDist - accumulated) / segLen;
                var pt = new VectorPoint(
                    points[i - 1].X + t * (points[i].X - points[i - 1].X),
                    points[i - 1].Y + t * (points[i].Y - points[i - 1].Y));
                var tan = Normalize(new VectorPoint(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y));
                return (pt, tan);
            }
            accumulated += segLen;
        }
        var last = points[^1];
        return (last, Normalize(new VectorPoint(last.X - points[^2].X, last.Y - points[^2].Y)));
    }

    private static (VectorPoint Point, VectorPoint Tangent) PointAtDistance(List<(VectorPoint Point, VectorPoint Tangent)> samples, double dist)
    {
        double accumulated = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            double segLen = Distance(samples[i - 1].Point, samples[i].Point);
            if (accumulated + segLen >= dist)
            {
                double t = (dist - accumulated) / segLen;
                var pt = new VectorPoint(
                    samples[i - 1].Point.X + t * (samples[i].Point.X - samples[i - 1].Point.X),
                    samples[i - 1].Point.Y + t * (samples[i].Point.Y - samples[i - 1].Point.Y));
                return (pt, samples[i].Tangent);
            }
            accumulated += segLen;
        }
        return (samples[^1].Point, samples[^1].Tangent);
    }

    private static double Distance(VectorPoint a, VectorPoint b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static VectorPoint Normalize(VectorPoint p)
    {
        double len = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        return len < 1e-9 ? new VectorPoint(1, 0) : new VectorPoint(p.X / len, p.Y / len);
    }
}
