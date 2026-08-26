using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Node-edit model (engine-side node editing): add, delete,
/// move, and split operations on a polyline's points. UI binds to these.
/// </summary>
public static class NodeEditEngine
{
    /// <summary>Insert a point at the midpoint of the edge containing the given position.</summary>
    public static bool InsertPoint(List<VectorPoint> points, VectorPoint at, out int insertedIndex)
    {
        insertedIndex = -1;
        if (points.Count < 2) return false;
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double d = DistanceToSegment(at, points[i], points[i + 1]);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return false;
        var mid = new VectorPoint((points[best].X + points[best + 1].X) / 2, (points[best].Y + points[best + 1].Y) / 2);
        points.Insert(best + 1, mid);
        insertedIndex = best + 1;
        return true;
    }

    /// <summary>Remove the point nearest to `at` (keeps at least 2 points).</summary>
    public static bool DeletePoint(List<VectorPoint> points, VectorPoint at)
    {
        if (points.Count <= 2) return false;
        int idx = NearestIndex(points, at);
        if (idx < 0) return false;
        points.RemoveAt(idx);
        return true;
    }

    /// <summary>Move the point nearest to `at` to `to`.</summary>
    public static bool MovePoint(List<VectorPoint> points, VectorPoint at, VectorPoint to)
    {
        int idx = NearestIndex(points, at);
        if (idx < 0) return false;
        points[idx] = to;
        return true;
    }

    /// <summary>Split the edge nearest to `at` into two edges (insert a point at `at` projected on the edge).</summary>
    public static bool SplitEdge(List<VectorPoint> points, VectorPoint at, out int insertedIndex)
    {
        insertedIndex = -1;
        if (points.Count < 2) return false;
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double d = DistanceToSegment(at, points[i], points[i + 1]);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return false;
        var proj = Project(at, points[best], points[best + 1]);
        points.Insert(best + 1, proj);
        insertedIndex = best + 1;
        return true;
    }

    public static int NearestIndex(List<VectorPoint> points, VectorPoint at)
    {
        if (points.Count == 0) return -1;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            double d = points[i].DistanceTo(at);
            if (d < bestD) { bestD = d; best = i; }
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

    public static VectorPoint Project(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-12) return a;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        return new VectorPoint(a.X + t * dx, a.Y + t * dy);
    }
}
