using VectorPilot.Geometry;

namespace VectorPilot.Geometry;

/// <summary>
/// Rounds the corners of selected vectors (ported from FilletExtend.swift, SPK-0215).
/// Freehand polylines get every applicable interior vertex rounded; rectangles
/// convert to a rounded freehand polyline. The fillet arc is sampled as points.
/// </summary>
public static class ShapeFilletEngine
{
    /// <summary>Fillet every applicable corner of a shape. Circles/ellipses/arcs/lines unchanged.</summary>
    public static VectorShape Fillet(VectorShape shape, double radius)
    {
        if (radius <= 1e-9) return shape;
        switch (shape.Type)
        {
            case ShapeType.Polyline when shape.Points.Count >= 3:
                return VectorShape.Polyline(FilletPolyline(shape.Points, radius), shape.Closed);
            case ShapeType.Rectangle when shape.Points.Count >= 2:
            {
                var b = shape.Bounds();
                var corners = new List<VectorPoint>
                {
                    new(b.MinX, b.MinY), new(b.MaxX, b.MinY),
                    new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY)
                };
                return VectorShape.Polyline(FilletPolyline(corners.Concat(new[] { corners[0] }).ToList(), radius), closed: true);
            }
            default:
                return shape;
        }
    }

    /// <summary>Round every corner of a polyline. Closed loops (first == last) get the
    /// wrap vertex rounded too; the radius clamps to fit short segments.</summary>
    public static List<VectorPoint> FilletPolyline(List<VectorPoint> points, double radius)
    {
        if (points.Count < 3 || radius <= 1e-9) return points;
        bool closed = points[0] == points[^1];
        int n = closed ? points.Count - 1 : points.Count;
        if (n < 3) return points;

        List<VectorPoint>? FilletCorner(VectorPoint prev, VectorPoint cur, VectorPoint next)
        {
            double ux = cur.X - prev.X, uy = cur.Y - prev.Y;
            double vx = next.X - cur.X, vy = next.Y - cur.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy);
            double lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu <= 1e-9 || lv <= 1e-9) return null;
            double u0 = ux / lu, u1 = uy / lu;
            double v0 = vx / lv, v1 = vy / lv;
            double dotUV = Math.Clamp(u0 * v0 + u1 * v1, -1.0, 1.0);
            double theta = Math.Acos(dotUV);
            if (theta <= 0.05 || theta >= Math.PI - 0.05) return null;
            double d = radius / Math.Tan(theta / 2);
            double maxD = Math.Min(lu, lv) * 0.49;
            if (d > maxD) d = maxD;
            if (d <= 1e-6) return null;
            var tA = new VectorPoint(cur.X - d * u0, cur.Y - d * u1);
            var tB = new VectorPoint(cur.X + d * v0, cur.Y + d * v1);
            double nx = -u1, ny = u0;
            double mx = -v1, my = v0;
            double denom = nx * my - ny * mx;
            if (Math.Abs(denom) <= 1e-12) return null;
            double s = ((tB.X - tA.X) * my - (tB.Y - tA.Y) * mx) / denom;
            double cx = tA.X + s * nx;
            double cy = tA.Y + s * ny;
            double rC = Math.Sqrt(Math.Pow(tA.X - cx, 2) + Math.Pow(tA.Y - cy, 2));
            double a0 = Math.Atan2(tA.Y - cy, tA.X - cx);
            double turn = u0 * v1 - u1 * v0;
            double sweep = turn >= 0 ? theta : -theta;
            int nArc = Math.Max(3, Math.Min(24, (int)Math.Ceiling(theta / (Math.PI / 8)) + 1));
            var arc = new List<VectorPoint>();
            for (int k = 1; k < nArc; k++)
            {
                double a = a0 + sweep * k / nArc;
                arc.Add(new VectorPoint(cx + rC * Math.Cos(a), cy + rC * Math.Sin(a)));
            }
            arc.Insert(0, tA);
            arc.Add(tB);
            return arc;
        }

        if (closed)
        {
            var loop = points.Take(n).ToList();
            var out_ = new List<VectorPoint>();
            for (int i = 0; i < n; i++)
            {
                var prev = loop[(i - 1 + n) % n];
                var cur = loop[i];
                var next = loop[(i + 1) % n];
                var arc = FilletCorner(prev, cur, next);
                if (arc is not null) out_.AddRange(arc);
                else out_.Add(cur);
            }
            out_.Add(out_[0]);
            return out_;
        }
        else
        {
            var out_ = new List<VectorPoint> { points[0] };
            for (int i = 1; i < n - 1; i++)
            {
                var arc = FilletCorner(points[i - 1], points[i], points[i + 1]);
                if (arc is not null) out_.AddRange(arc);
                else out_.Add(points[i]);
            }
            out_.Add(points[n - 1]);
            return out_;
        }
    }
}

/// <summary>Extends the open ends of selected vectors (ported from ShapeExtendEngine.swift).</summary>
public static class ShapeExtendEngine
{
    public static VectorShape Extend(VectorShape shape, double distance)
    {
        if (distance <= 1e-9) return shape;
        if (shape.Type == ShapeType.Line && shape.Points.Count >= 2)
        {
            var s = shape.Points[0];
            var e = shape.Points[1];
            double ux = e.X - s.X, uy = e.Y - s.Y;
            double l = Math.Sqrt(ux * ux + uy * uy);
            if (l <= 1e-9) return shape;
            return VectorShape.Line(
                new VectorPoint(s.X - ux / l * distance, s.Y - uy / l * distance),
                new VectorPoint(e.X + ux / l * distance, e.Y + uy / l * distance));
        }
        if (shape.Type == ShapeType.Polyline && shape.Points.Count >= 2 && shape.Points[0] != shape.Points[^1])
        {
            var out_ = shape.Points.ToList();
            var f = out_[0];
            var s = out_[1];
            double ux = f.X - s.X, uy = f.Y - s.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy);
            if (lu > 1e-9) out_[0] = new VectorPoint(f.X + ux / lu * distance, f.Y + uy / lu * distance);
            var last = out_[^1];
            var sl = out_[^2];
            double vx = last.X - sl.X, vy = last.Y - sl.Y;
            double lv = Math.Sqrt(vx * vx + vy * vy);
            if (lv > 1e-9) out_[^1] = new VectorPoint(last.X + vx / lv * distance, last.Y + vy / lv * distance);
            return VectorShape.Polyline(out_, closed: false);
        }
        return shape;
    }
}

/// <summary>Single-corner fillet + extend-line (ported from FilletExtendEngine.swift).</summary>
public static class FilletExtendEngine
{
    /// <summary>Fillet the corner nearest to cornerPoint; returns the outline exploded into line segments.</summary>
    public static List<VectorShape> Fillet(VectorShape shape, VectorPoint cornerPoint, double radius)
    {
        List<VectorPoint> loop;
        if (shape.Type == ShapeType.Polyline && shape.Points.Count >= 3)
        {
            loop = shape.Points.ToList();
        }
        else if (shape.Type == ShapeType.Rectangle && shape.Points.Count >= 2)
        {
            var b = shape.Bounds();
            loop = new List<VectorPoint>
            {
                new(b.MinX, b.MinY), new(b.MaxX, b.MinY), new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY), new(b.MinX, b.MinY)
            };
        }
        else
        {
            return new List<VectorShape> { shape };
        }

        int n = loop[0] == loop[^1] ? loop.Count - 1 : loop.Count;
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double d = loop[i].DistanceTo(cornerPoint);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best < 0) return new List<VectorShape> { shape };

        var prev = loop[(best - 1 + n) % n];
        var cur = loop[best];
        var next = loop[(best + 1) % n];
        var window = ShapeFilletEngine.FilletPolyline(new List<VectorPoint> { prev, cur, next }, radius);
        if (window.Count <= 3) return new List<VectorShape> { shape };

        var out_ = loop.ToList();
        var replacement = window.Skip(1).Take(window.Count - 2).ToList();
        if (loop[0] == loop[^1] && best == 0)
        {
            out_.RemoveAt(0);
            out_.InsertRange(0, replacement);
            out_[^1] = out_[0];
        }
        else
        {
            out_.RemoveAt(best);
            out_.InsertRange(best, replacement);
        }

        int segCount = out_[0] == out_[^1] ? out_.Count - 1 : out_.Count;
        var segments = new List<VectorShape>();
        for (int i = 0; i < segCount - 1; i++)
        {
            segments.Add(VectorShape.Line(out_[i], out_[i + 1]));
        }
        if (out_[0] == out_[^1] && segCount >= 2)
        {
            segments.Add(VectorShape.Line(out_[segCount - 1], out_[0]));
        }
        return segments;
    }

    /// <summary>Extend a line so one endpoint reaches `point` (projected onto the line direction).</summary>
    public static List<VectorShape> ExtendLine(VectorShape line, VectorPoint point)
    {
        if (line.Type != ShapeType.Line || line.Points.Count < 2) return new List<VectorShape> { line };
        var s = line.Points[0];
        var e = line.Points[1];
        double ux = e.X - s.X, uy = e.Y - s.Y;
        double l = Math.Sqrt(ux * ux + uy * uy);
        if (l <= 1e-9) return new List<VectorShape> { line };
        double u0 = ux / l, u1 = uy / l;
        double projEnd = (point.X - s.X) * u0 + (point.Y - s.Y) * u1;
        if (projEnd > l) return new List<VectorShape> { VectorShape.Line(s, point) };
        if (projEnd < 0) return new List<VectorShape> { VectorShape.Line(point, e) };
        return new List<VectorShape> { line };
    }
}
