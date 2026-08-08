using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Vector texture (Aspire parity; clean implementation): fills the area
/// inside a boundary with a repeating pattern of vectors — crosshatch,
/// dots, or a custom tile — producing decorative relief-style engraving.
/// </summary>
public static class VectorTextureEngine
{
    public enum PatternKind { Crosshatch, Dots, Zigzag }

    public sealed class Params
    {
        public PatternKind Pattern { get; set; } = PatternKind.Crosshatch;
        public double SpacingMm { get; set; } = 4.0;
        public double AngleDegrees { get; set; }
        public double DotDiameterMm { get; set; } = 1.0;
        public bool ClipToBoundary { get; set; } = true;
    }

    /// <summary>Generate the pattern vectors inside each closed boundary.</summary>
    public static List<VectorShape> Generate(IReadOnlyList<VectorShape> boundaries, Params p)
    {
        var shapes = new List<VectorShape>();
        double spacing = Math.Max(0.1, p.SpacingMm);

        foreach (var boundary in boundaries)
        {
            if (SpecialtyBoundary.PolygonPoints(boundary) is not { } poly) continue;
            var b = boundary.Bounds();
            double minX = b.MinX, maxX = b.MaxX, minY = b.MinY, maxY = b.MaxY;

            switch (p.Pattern)
            {
                case PatternKind.Crosshatch:
                {
                    double a = GeometryMath.DegToRad(p.AngleDegrees);
                    double cosA = Math.Cos(a), sinA = Math.Sin(a);
                    // Rotate the boundary so lines are horizontal, scan, rotate back.
                    var rotated = poly.Select(pt => new VectorPoint(pt.X * cosA - pt.Y * sinA, pt.X * sinA + pt.Y * cosA)).ToList();
                    double yMin = rotated.Min(pt => pt.Y), yMax = rotated.Max(pt => pt.Y);
                    double y = yMin + spacing / 2;
                    while (y < yMax)
                    {
                        foreach (var run in SpecialtyBoundary.InsideRuns(rotated, y))
                        {
                            var s = new VectorPoint(run.X0 * cosA + y * sinA, -run.X0 * sinA + y * cosA);
                            var e = new VectorPoint(run.X1 * cosA + y * sinA, -run.X1 * sinA + y * cosA);
                            shapes.Add(VectorShape.Line(s, e));
                        }
                        y += spacing;
                    }
                    break;
                }
                case PatternKind.Dots:
                {
                    double step = Math.Max(p.DotDiameterMm * 1.5, spacing);
                    for (double y = minY + step / 2; y < maxY; y += step)
                    {
                        for (double x = minX + step / 2; x < maxX; x += step)
                        {
                            var dot = ShapeFactory.EllipsePoints(new VectorPoint(x, y), p.DotDiameterMm / 2, p.DotDiameterMm / 2, 0, 16);
                            shapes.Add(VectorShape.Polyline(dot, closed: true));
                        }
                    }
                    break;
                }
                case PatternKind.Zigzag:
                {
                    double amplitude = Math.Max(0.5, spacing * 0.4);
                    for (double y = minY + spacing; y < maxY; y += spacing)
                    {
                        var pts = new List<VectorPoint>();
                        bool up = true;
                        for (double x = minX; x <= maxX; x += spacing / 2)
                        {
                            pts.Add(new VectorPoint(x, y + (up ? amplitude : -amplitude)));
                            up = !up;
                        }
                        shapes.Add(VectorShape.Polyline(pts, closed: false));
                    }
                    break;
                }
            }
        }

        if (p.ClipToBoundary)
        {
            // Clip pattern lines to their boundary: keep only segments whose
            // midpoint is inside the polygon (even-odd).
            var clipped = new List<VectorShape>();
            foreach (var shape in shapes)
            {
                if (shape.Type == ShapeType.Line && shape.Points.Count >= 2)
                {
                    var mid = new VectorPoint((shape.Points[0].X + shape.Points[1].X) / 2, (shape.Points[0].Y + shape.Points[1].Y) / 2);
                    if (ContainsBoundary(boundaries, mid)) clipped.Add(shape);
                }
                else
                {
                    clipped.Add(shape);
                }
            }
            return clipped;
        }
        return shapes;
    }

    private static bool ContainsBoundary(IReadOnlyList<VectorShape> boundaries, VectorPoint p)
    {
        foreach (var b in boundaries)
        {
            if (Contains(b, p)) return true;
        }
        return false;
    }

    private static bool Contains(VectorShape boundary, VectorPoint p)
    {
        if (SpecialtyBoundary.PolygonPoints(boundary) is not { } poly) return false;
        int n = poly.Count - 1;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = poly[i];
            var pj = poly[j];
            if ((pi.Y > p.Y) != (pj.Y > p.Y) &&
                p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
