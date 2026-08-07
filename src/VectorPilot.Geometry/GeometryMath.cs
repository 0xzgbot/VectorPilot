namespace VectorPilot.Geometry;

/// <summary>Pure geometry math used by the engine (mirrors ShopPilotGeometry helpers).</summary>
public static class GeometryMath
{
    public const double TwoPi = Math.PI * 2;

    /// <summary>Distance from point p to the segment a-b (squared).</summary>
    public static double DistanceSqToSegment(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return p.DistanceTo(a) * p.DistanceTo(a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        double ex = p.X - cx, ey = p.Y - cy;
        return ex * ex + ey * ey;
    }

    /// <summary>Signed area; positive = clockwise in screen coords (y-down).</summary>
    public static double SignedArea(IReadOnlyList<VectorPoint> pts)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2.0;
    }

    /// <summary>Sample a circle/arc into a polyline of N segments.</summary>
    public static List<VectorPoint> SampleArc(VectorPoint center, double radius, double startDeg, double endDeg, int segments = 48)
    {
        var pts = new List<VectorPoint>();
        double start = startDeg * Math.PI / 180.0, end = endDeg * Math.PI / 180.0;
        if (Math.Abs(end - start) < 1e-9) end = start + TwoPi;
        for (int i = 0; i <= segments; i++)
        {
            double t = start + (end - start) * i / segments;
            pts.Add(new VectorPoint(center.X + radius * Math.Cos(t), center.Y + radius * Math.Sin(t)));
        }
        return pts;
    }

    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Normalize an angle to [0, 360).</summary>
    public static double NormalizeDeg(double deg)
    {
        double r = deg % 360.0;
        return r < 0 ? r + 360.0 : r;
    }
}
