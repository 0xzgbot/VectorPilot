using VectorPilot.Geometry;

namespace VectorPilot.Engine;

public enum SweepProfile { Rectangle, Circle, Custom, Path }

/// <summary>
/// Two-rail sweep relief (ported from SweepReliefEngine.swift, SPK-0714):
/// sweeps a profile between two rails and rasterizes onto a heightfield.
/// Rails are re-sampled by length fraction; rectangle profile = flat top,
/// circle profile = domed top (full height on the centerline).
/// </summary>
public static class SweepReliefEngine
{
    public static HeightfieldData? Sweep(
        IReadOnlyList<VectorPoint> rail1,
        IReadOnlyList<VectorPoint> rail2,
        SweepProfile profile,
        double height,
        double cellSizeMm = 1.0,
        int samples = 40)
    {
        if (rail1.Count < 2 || rail2.Count < 2) return null;
        var a = Resample(rail1, Math.Max(4, samples));
        var b = Resample(rail2, Math.Max(4, samples));
        if (a.Count != b.Count) return null;

        var strip = new List<VectorPoint>(a);
        strip.AddRange(b.AsEnumerable().Reverse());
        if (strip.Count < 4) return null;

        double minX = strip.Min(p => p.X), maxX = strip.Max(p => p.X);
        double minY = strip.Min(p => p.Y), maxY = strip.Max(p => p.Y);
        int cols = Math.Max(2, (int)Math.Round((maxX - minX) / cellSizeMm));
        int rows = Math.Max(2, (int)Math.Round((maxY - minY) / cellSizeMm));
        double peak = Math.Max(0, height);
        var heights = new double[cols * rows];

        var centerline = profile == SweepProfile.Circle ? MidpointPolyline(a, b) : null;

        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                double wx = minX + (i + 0.5) * cellSizeMm;
                double wy = minY + (j + 0.5) * cellSizeMm;
                var p = new VectorPoint(wx, wy);
                if (!PointInPolygon(p, strip)) continue;
                if (profile == SweepProfile.Circle && centerline is not null)
                {
                    double d = DistanceToPolyline(p, centerline);
                    double halfWidth = LocalHalfWidth(p, centerline, a, b);
                    double t = halfWidth > 1e-9 ? Math.Min(1.0, d / halfWidth) : 1.0;
                    heights[j * cols + i] = peak * Math.Max(0, 1 - t);
                }
                else
                {
                    heights[j * cols + i] = peak;
                }
            }
        }

        return new HeightfieldData(cols, rows, cellSizeMm, minX, minY, heights);
    }

    /// <summary>Re-sample a polyline to `count` points by cumulative length fraction.</summary>
    public static List<VectorPoint> Resample(IReadOnlyList<VectorPoint> points, int count)
    {
        if (points.Count < 2) return points.ToList();
        var cumulative = CumulativeLengths(points);
        double total = cumulative[^1];
        if (total <= 1e-9) return points.ToList();
        var out_ = new List<VectorPoint>();
        int seg = 0;
        for (int k = 0; k < count; k++)
        {
            double target = total * k / (count - 1);
            while (seg < cumulative.Count - 2 && cumulative[seg + 1] < target - 1e-12)
            {
                seg++;
            }
            double segLen = cumulative[seg + 1] - cumulative[seg];
            double t = segLen > 1e-12 ? (target - cumulative[seg]) / segLen : 0;
            var p0 = points[seg];
            var p1 = points[Math.Min(seg + 1, points.Count - 1)];
            out_.Add(new VectorPoint(p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t));
        }
        return out_;
    }

    public static List<double> CumulativeLengths(IReadOnlyList<VectorPoint> points)
    {
        var out_ = new List<double> { 0 };
        for (int i = 1; i < points.Count; i++)
        {
            out_.Add(out_[i - 1] + points[i - 1].DistanceTo(points[i]));
        }
        return out_;
    }

    public static double DistanceToPolyline(VectorPoint p, IReadOnlyList<VectorPoint> poly)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count - 1; i++)
        {
            best = Math.Min(best, DistanceToSegment(p, poly[i], poly[i + 1]));
        }
        return best;
    }

    public static double DistanceToSegment(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-12) return p.DistanceTo(a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        return p.DistanceTo(new VectorPoint(a.X + t * dx, a.Y + t * dy));
    }

    public static List<VectorPoint> MidpointPolyline(IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b)
    {
        var out_ = new List<VectorPoint>();
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            out_.Add(new VectorPoint((a[i].X + b[i].X) / 2, (a[i].Y + b[i].Y) / 2));
        }
        return out_;
    }

    /// <summary>Half the rail separation at the cell's nearest centerline station.</summary>
    public static double LocalHalfWidth(VectorPoint p, IReadOnlyList<VectorPoint> centerline, IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b)
    {
        if (centerline.Count < 2) return 1.0;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < centerline.Count; i++)
        {
            double d = p.DistanceTo(centerline[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        var ai = Math.Min(best, a.Count - 1);
        var bi = Math.Min(best, b.Count - 1);
        return Math.Max(0.001, a[ai].DistanceTo(b[bi]) / 2.0);
    }

    /// <summary>Even-odd ray-cast point-in-polygon.</summary>
    public static bool PointInPolygon(VectorPoint p, IReadOnlyList<VectorPoint> poly)
    {
        bool inside = false;
        int j = poly.Count - 1;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                double xCross = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                if (p.X < xCross) inside = !inside;
            }
            j = i;
        }
        return inside;
    }
}
