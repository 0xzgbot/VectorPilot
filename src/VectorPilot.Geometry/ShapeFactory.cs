using VectorPilot.Geometry;

namespace VectorPilot.Geometry;

/// <summary>
/// Draw-tool shape factories (Aspire draw-tool parity): arc, polygon, star,
/// spiral, ellipse generators as point sets / VectorShapes.
/// </summary>
public static class ShapeFactory
{
    /// <summary>Arc polyline from center, radius, start/end degrees (CCW when end > start).</summary>
    public static List<VectorPoint> ArcPoints(VectorPoint center, double radius, double startDeg, double endDeg, int segments = 48)
    {
        var pts = new List<VectorPoint>();
        int n = Math.Max(8, segments);
        for (int i = 0; i <= n; i++)
        {
            double a = GeometryMath.DegToRad(startDeg + (endDeg - startDeg) * i / n);
            pts.Add(new VectorPoint(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Regular polygon (N sides, circumradius r, rotation degrees).</summary>
    public static List<VectorPoint> RegularPolygon(VectorPoint center, double radius, int sides, double rotationDeg = 0)
    {
        if (sides < 3) return new List<VectorPoint>();
        var pts = new List<VectorPoint>();
        for (int i = 0; i < sides; i++)
        {
            double a = GeometryMath.DegToRad(rotationDeg + 360.0 * i / sides);
            pts.Add(new VectorPoint(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Star (points spikes, outer/inner radius, rotation degrees).</summary>
    public static List<VectorPoint> Star(VectorPoint center, double outerRadius, double innerRadius, int points, double rotationDeg = 0)
    {
        if (points < 2) return new List<VectorPoint>();
        var pts = new List<VectorPoint>();
        for (int i = 0; i < points * 2; i++)
        {
            double r = i % 2 == 0 ? outerRadius : innerRadius;
            double a = GeometryMath.DegToRad(rotationDeg + 180.0 * i / points);
            pts.Add(new VectorPoint(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Archimedean spiral from radius r0 to r1 over turns.</summary>
    public static List<VectorPoint> Spiral(VectorPoint center, double innerRadius, double outerRadius, double turns, double rotationDeg = 0, int segments = 120)
    {
        var pts = new List<VectorPoint>();
        int n = Math.Max(32, segments);
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            double r = innerRadius + (outerRadius - innerRadius) * t;
            double a = GeometryMath.DegToRad(rotationDeg + 360.0 * turns * t);
            pts.Add(new VectorPoint(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Ellipse points (rx, ry, rotation degrees).</summary>
    public static List<VectorPoint> EllipsePoints(VectorPoint center, double rx, double ry, double rotationDeg = 0, int segments = 64)
    {
        var pts = new List<VectorPoint>();
        double rot = GeometryMath.DegToRad(rotationDeg);
        double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
        int n = Math.Max(16, segments);
        for (int i = 0; i <= n; i++)
        {
            double a = 2 * Math.PI * i / n;
            double ex = rx * Math.Cos(a), ey = ry * Math.Sin(a);
            pts.Add(new VectorPoint(center.X + ex * cosR - ey * sinR, center.Y + ex * sinR + ey * cosR));
        }
        return pts;
    }

    /// <summary>Closed VectorShape from a point set.</summary>
    public static VectorShape ClosedPolyline(List<VectorPoint> pts) => VectorShape.Polyline(pts, closed: true);
}
