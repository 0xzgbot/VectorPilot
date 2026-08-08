namespace VectorPilot.Geometry;

/// <summary>Result of offsetting a shape by a signed distance (port of VectorOffsetCalculator.swift).</summary>
public sealed class OffsetResult
{
    public VectorShape Original { get; }
    public List<VectorPoint> OffsetPath { get; }
    public double Distance { get; }
    public bool IsValid => OffsetPath.Count > 0;

    public OffsetResult(VectorShape original, List<VectorPoint> offsetPath, double distance)
    {
        Original = original;
        OffsetPath = offsetPath;
        Distance = distance;
    }
}

/// <summary>
/// Parallel offset operations per shape type. Positive distance = outward expansion
/// regardless of winding; negative = inward. Ported from ShopPilot VectorOffset.swift.
/// </summary>
public static class VectorOffset
{
    private const int ArcSampleCount = 64;
    private const double Eps = 1e-9;

    /// <summary>Normalise an angle to [0, 2π).</summary>
    private static double NormaliseAngle(double angle)
    {
        var a = angle % (Math.PI * 2);
        if (a < 0) a += Math.PI * 2;
        return a;
    }

    /// <summary>
    /// Evenly-spaced points along an arc. Full-circle calls (span ≥ 2π) sample the
    /// whole circumference — the raw span is checked BEFORE normalisation so a
    /// 0→2π input is not treated as a zero-sweep degenerate arc.
    /// </summary>
    public static List<VectorPoint> SampleArcPoints(VectorPoint center, double radius, double startAngle, double endAngle)
    {
        if (radius <= 1e-9) return new List<VectorPoint>();

        double sa = NormaliseAngle(startAngle);
        double ea = NormaliseAngle(endAngle);
        double rawSpan = endAngle - startAngle;

        double sweep;
        if (Math.Abs(rawSpan) >= Math.PI * 2 - 1e-9)
        {
            sweep = rawSpan > 0 ? Math.PI * 2 : -Math.PI * 2;
        }
        else if (ea >= sa)
        {
            sweep = ea - sa;
        }
        else
        {
            sweep = Math.PI * 2 - (sa - ea);
        }

        if (Math.Abs(sweep) <= 1e-9)
        {
            return new List<VectorPoint> { new(center.X + radius, center.Y) };
        }

        var points = new List<VectorPoint>();
        int count = Math.Max(2, ArcSampleCount);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / (count - 1);
            double angle = sa + sweep * t;
            points.Add(new VectorPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }
        return points;
    }

    public static OffsetResult? OffsetLine(VectorShape line, double distance)
    {
        if (line.Type != ShapeType.Line || line.Points.Count < 2) return null;
        var start = line.Points[0];
        var end = line.Points[1];

        double dx = end.X - start.X, dy = end.Y - start.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-9)
        {
            return new OffsetResult(line, new List<VectorPoint> { new(start.X - distance, start.Y) }, distance);
        }

        double len = Math.Sqrt(lenSq);
        double nx = -dy / len, ny = dx / len; // left normal
        var p1 = new VectorPoint(start.X + nx * distance, start.Y + ny * distance);
        var p2 = new VectorPoint(end.X + nx * distance, end.Y + ny * distance);
        return new OffsetResult(line, new List<VectorPoint> { p1, p2 }, distance);
    }

    public static OffsetResult? OffsetCircle(VectorShape circle, double distance)
    {
        if (circle.Type != ShapeType.Circle || circle.Points.Count < 1) return null;
        var center = circle.Points[0];
        double newRadius = circle.Radius + distance;
        if (newRadius <= 1e-9)
        {
            return new OffsetResult(circle, new List<VectorPoint> { center }, distance);
        }
        var points = SampleArcPoints(center, newRadius, 0, Math.PI * 2);
        return new OffsetResult(circle, points, distance);
    }

    public static OffsetResult? OffsetRectangle(VectorShape rect, double distance)
    {
        if (rect.Type != ShapeType.Rectangle || rect.Points.Count < 2) return null;
        var b = rect.Bounds();
        double minX = b.MinX, maxX = b.MaxX, minY = b.MinY, maxY = b.MaxY;

        var p1 = new VectorPoint(minX - distance, minY - distance);
        var p2 = new VectorPoint(maxX + distance, minY - distance);
        var p3 = new VectorPoint(maxX + distance, maxY + distance);
        var p4 = new VectorPoint(minX - distance, maxY + distance);

        double newW = (maxX + distance) - (minX - distance);
        double newH = (maxY + distance) - (minY - distance);
        if (newW <= 1e-9 || newH <= 1e-9)
        {
            return new OffsetResult(rect, new List<VectorPoint>(), distance);
        }

        var path = new List<VectorPoint> { p1, p2, p3, p4, p1 };
        return new OffsetResult(rect, path, distance);
    }

    public static OffsetResult? OffsetArc(VectorShape arc, double distance)
    {
        if (arc.Type != ShapeType.Arc || arc.Points.Count < 1) return null;
        var center = arc.Points[0];
        double newRadius = arc.Radius + distance;
        if (newRadius <= 1e-9)
        {
            return new OffsetResult(arc, new List<VectorPoint> { center }, distance);
        }
        var points = SampleArcPoints(center, newRadius, arc.StartAngleDeg * Math.PI / 180.0, arc.EndAngleDeg * Math.PI / 180.0);
        return new OffsetResult(arc, points, distance);
    }

    /// <summary>
    /// Offset a closed polyline with winding-aware miter joins. Positive = outward,
    /// negative = inward, regardless of winding. Ported from VectorOffset.swift.
    /// Returns null when the polygon collapses or has fewer than 3 corners.
    /// </summary>
    public static OffsetResult? OffsetClosedPolyline(IReadOnlyList<VectorPoint> points, double distance)
    {
        if (points.Count < 3) return null;

        var pts = points.ToList();
        if (pts.Count > 1 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) return null;

        // Winding via shoelace: positive = CCW.
        double area2 = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            area2 += a.X * b.Y - b.X * a.Y;
        }
        bool isCCW = area2 > 0;

        // Outward unit normal of edge (from → to): CCW → right normal; CW → left normal.
        static (double X, double Y)? OutwardNormal(VectorPoint from, VectorPoint to, bool ccw)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-9) return null;
            return ccw ? (dy / len, -dx / len) : (-dy / len, dx / len);
        }

        // Miter join: v' = v + d * (n1 + n2) / (1 + n1·n2).
        var offsetVerts = new List<VectorPoint>();
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = pts[(i - 1 + n) % n];
            var curr = pts[i];
            var next = pts[(i + 1) % n];

            var n1 = OutwardNormal(prev, curr, isCCW);
            var n2 = OutwardNormal(curr, next, isCCW);
            if (n1 is null || n2 is null) continue;

            double dot = n1.Value.X * n2.Value.X + n1.Value.Y * n2.Value.Y;
            double denom = 1.0 + dot;
            if (Math.Abs(denom) <= 1e-9) continue; // near-collinear spike

            double scale = distance / denom;
            offsetVerts.Add(new VectorPoint(
                curr.X + (n1.Value.X + n2.Value.X) * scale,
                curr.Y + (n1.Value.Y + n2.Value.Y) * scale));
        }

        if (offsetVerts.Count < 3) return null;

        var path = new List<VectorPoint>(offsetVerts) { offsetVerts[0] };
        var shape = VectorShape.Polyline(points, closed: true);
        return new OffsetResult(shape, path, distance);
    }

    /// <summary>Offset any supported shape, returning concrete shapes the editor can commit.</summary>
    public static List<VectorShape> OffsetShape(VectorShape shape, double distance)
    {
        switch (shape.Type)
        {
            case ShapeType.Line:
            {
                var r = OffsetLine(shape, distance);
                return r is { OffsetPath.Count: >= 2 } ? new List<VectorShape> { VectorShape.Polyline(r.OffsetPath) } : new List<VectorShape>();
            }
            case ShapeType.Circle:
            {
                double newRadius = shape.Radius + distance;
                return newRadius > 1e-9
                    ? new List<VectorShape> { VectorShape.Circle(shape.Points[0], newRadius) }
                    : new List<VectorShape>();
            }
            case ShapeType.Rectangle:
            {
                var r = OffsetRectangle(shape, distance);
                if (r is not { OffsetPath.Count: >= 4 }) return new List<VectorShape>();
                var xs = r.OffsetPath.Select(p => p.X).ToArray();
                var ys = r.OffsetPath.Select(p => p.Y).ToArray();
                double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
                if (maxX - minX <= 1e-9 || maxY - minY <= 1e-9) return new List<VectorShape>();
                return new List<VectorShape> { VectorShape.Rectangle(minX, minY, maxX - minX, maxY - minY) };
            }
            case ShapeType.Arc:
            {
                var r = OffsetArc(shape, distance);
                return r is { OffsetPath.Count: >= 2 } ? new List<VectorShape> { VectorShape.Polyline(r.OffsetPath) } : new List<VectorShape>();
            }
            case ShapeType.Polyline:
            {
                var r = OffsetClosedPolyline(shape.Points, distance);
                return r is { OffsetPath.Count: >= 3 } ? new List<VectorShape> { VectorShape.Polyline(r.OffsetPath, closed: true) } : new List<VectorShape>();
            }
            default:
                return new List<VectorShape>();
        }
    }
}
