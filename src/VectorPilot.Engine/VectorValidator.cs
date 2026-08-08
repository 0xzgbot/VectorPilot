using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Severity of a vector problem (Aspire vector-validator parity).</summary>
public enum VectorIssueSeverity { Warning, Error }

/// <summary>A problem found on a vector (open loop, self-intersection, degenerate).</summary>
public sealed class VectorIssue
{
    public VectorIssueSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public int ShapeIndex { get; init; }
    public VectorPoint? Location { get; init; }
}

/// <summary>
/// Vector validator (Aspire parity; clean implementation): detects open
/// loops, self-intersections, duplicate points, and degenerate shapes.
/// </summary>
public static class VectorValidator
{
    /// <summary>Check every shape; returns all issues found.</summary>
    public static List<VectorIssue> Validate(IReadOnlyList<VectorShape> shapes)
    {
        var issues = new List<VectorIssue>();
        for (int i = 0; i < shapes.Count; i++)
        {
            var s = shapes[i];
            if (s.Points.Count == 0)
            {
                issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Error, Message = "Empty shape (no points)", ShapeIndex = i });
                continue;
            }
            if (s.Points.Count == 1)
            {
                issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Error, Message = "Single-point shape", ShapeIndex = i, Location = s.Points[0] });
                continue;
            }
            if (s.Type is ShapeType.Line or ShapeType.Polyline && s.Points.Count == 2)
            {
                // A two-point polyline flagged closed is an open loop.
                if (s.Closed && s.Points[0] != s.Points[^1])
                {
                    issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Error, Message = "Two-point shape marked closed is an open loop", ShapeIndex = i });
                }
                continue;
            }

            bool isLoop = s.Closed || (s.Points[0] == s.Points[^1]);
            if (isLoop)
            {
                // Duplicate consecutive points (degenerate edges).
                for (int k = 1; k < s.Points.Count; k++)
                {
                    if (s.Points[k - 1].DistanceTo(s.Points[k]) < 1e-9)
                    {
                        issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Warning, Message = $"Duplicate consecutive point at index {k}", ShapeIndex = i, Location = s.Points[k] });
                        break;
                    }
                }
                // Self-intersection (non-adjacent edge pairs).
                if (HasSelfIntersection(s))
                {
                    issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Warning, Message = "Shape self-intersects", ShapeIndex = i });
                }
            }
            else if (s.Points.Count >= 3 && s.Type != ShapeType.Line)
            {
                // A polyline with 3+ points that doesn't close is a candidate open loop.
                issues.Add(new VectorIssue { Severity = VectorIssueSeverity.Warning, Message = "Open loop — endpoints do not meet", ShapeIndex = i, Location = s.Points[^1] });
            }
        }
        return issues;
    }

    /// <summary>True when non-adjacent edges of a closed shape cross.</summary>
    public static bool HasSelfIntersection(VectorShape shape)
    {
        var pts = shape.Points;
        int n = shape.Closed ? pts.Count : pts.Count - 1;
        if (n < 4) return false;

        bool Adjacent(int a, int b) => a == b || Math.Abs(a - b) == 1 || (shape.Closed && (a == 0 && b == n - 1) || (a == n - 1 && b == 0));

        for (int i = 0; i < n; i++)
        {
            var a1 = pts[i];
            var a2 = pts[(i + 1) % pts.Count];
            for (int j = i + 1; j < n; j++)
            {
                if (Adjacent(i, j)) continue;
                var b1 = pts[j];
                var b2 = pts[(j + 1) % pts.Count];
                if (SegmentsCross(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static bool SegmentsCross(VectorPoint a1, VectorPoint a2, VectorPoint b1, VectorPoint b2)
    {
        double Cross(VectorPoint o, VectorPoint p, VectorPoint q)
            => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);

        double d1 = Cross(b1, b2, a1);
        double d2 = Cross(b1, b2, a2);
        double d3 = Cross(a1, a2, b1);
        double d4 = Cross(a1, a2, b2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
