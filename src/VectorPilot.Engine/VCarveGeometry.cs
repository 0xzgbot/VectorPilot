using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// V-carve depth geometry. Carving depth is set by the LOCAL CHANNEL WIDTH — how
/// much room the V-bit has between opposing edges — not by position on the page.
/// For a bit of included angle A, a channel of half-width w admits a depth of
/// w / tan(A/2): narrow gaps stay shallow, wide areas run to full depth.
/// </summary>
public static class VCarveGeometry
{
    /// <summary>
    /// Depth (negative Z) a V-bit reaches in a channel of the given half-width,
    /// clamped to <paramref name="maxDepth"/>.
    /// </summary>
    public static double DepthForHalfWidth(double halfWidth, double toolAngleDegrees, double maxDepth)
    {
        if (halfWidth <= 0) return 0;

        double halfAngleRad = Math.Max(1e-6, toolAngleDegrees / 2.0 * Math.PI / 180.0);
        double tan = Math.Tan(halfAngleRad);
        if (tan <= 1e-9) return -maxDepth;

        double depth = halfWidth / tan;
        return -Math.Min(depth, maxDepth);
    }

    /// <summary>
    /// Distance from point <paramref name="index"/> of <paramref name="vector"/> to
    /// the nearest edge that is not its own immediate neighbours — an approximation
    /// of the medial-axis radius at that point.
    /// </summary>
    public static double DistanceToNearestOtherEdge(
        VectorShape vector, int index, IReadOnlyList<VectorShape> allVectors)
    {
        if (index < 0 || index >= vector.Points.Count) return 0;
        var p = vector.Points[index];
        double best = double.MaxValue;

        foreach (var other in allVectors)
        {
            var pts = other.Points;
            if (pts.Count < 2) continue;
            bool same = ReferenceEquals(other, vector);

            int segCount = other.Closed ? pts.Count : pts.Count - 1;
            for (int s = 0; s < segCount; s++)
            {
                int a = s, b = (s + 1) % pts.Count;

                // Skip the two segments touching this point: they are always at
                // distance zero and say nothing about the channel width.
                if (same && (a == index || b == index)) continue;

                double d = PointToSegment(p, pts[a], pts[b]);
                if (d < best) best = d;
            }
        }

        return best == double.MaxValue ? 0 : best;
    }

    private static double PointToSegment(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-18) return Dist(p, a);

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        return Dist(p, new VectorPoint(a.X + t * dx, a.Y + t * dy));
    }

    private static double Dist(VectorPoint a, VectorPoint b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
