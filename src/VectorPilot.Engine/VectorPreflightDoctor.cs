using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Preflight-doctor issue kinds (SPK-0211/0212).</summary>
public enum VectorDoctorKind { OpenPath, SelfIntersection, Degenerate, Gap }

/// <summary>A preflight-doctor finding: plain-English title, severity, and the
/// exact affected shape indices (SPK-0211/0212 fix actions).</summary>
public sealed class VectorDoctorIssue
{
    public VectorDoctorKind Kind { get; init; }
    public string Title { get; init; } = "";
    public VectorDoctorSeverity Severity { get; init; }
    public List<int> ShapeIndices { get; init; } = new();
}

public enum VectorDoctorSeverity { Error, Warning, Info }

/// <summary>
/// Vector Preflight Doctor (ported from the SPK-0211/0212 contract): flags open
/// paths (error), self-intersections (warning), degenerate shapes (warning),
/// and near-but-not-touching gaps (info), each with the real shape indices.
/// A closed rectangle + far-away circle produce no issues.
/// </summary>
public static class VectorPreflightDoctor
{
    private const double GapThresholdMm = 1.0;

    public static List<VectorDoctorIssue> Check(IReadOnlyList<VectorShape> shapes)
    {
        var issues = new List<VectorDoctorIssue>();

        // 1. OPEN paths — error with the real index.
        for (int i = 0; i < shapes.Count; i++)
        {
            if (!shapes[i].Closed)
            {
                issues.Add(new VectorDoctorIssue
                {
                    Kind = VectorDoctorKind.OpenPath,
                    Title = "Open vector",
                    Severity = VectorDoctorSeverity.Error,
                    ShapeIndices = { i }
                });
            }
        }

        // 2. SELF-INTERSECTION — bowtie detection on closed shapes (warning).
        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i].Closed && HasSelfIntersection(shapes[i]))
            {
                issues.Add(new VectorDoctorIssue
                {
                    Kind = VectorDoctorKind.SelfIntersection,
                    Title = "Self-intersecting vector",
                    Severity = VectorDoctorSeverity.Warning,
                    ShapeIndices = { i }
                });
            }
        }

        // 3. DEGENERATE — zero-length line, zero-radius circle (warning).
        for (int i = 0; i < shapes.Count; i++)
        {
            if (IsDegenerate(shapes[i]))
            {
                issues.Add(new VectorDoctorIssue
                {
                    Kind = VectorDoctorKind.Degenerate,
                    Title = "Degenerate vector",
                    Severity = VectorDoctorSeverity.Warning,
                    ShapeIndices = { i }
                });
            }
        }

        // 4. GAP — near but not touching pairs (info).
        for (int i = 0; i < shapes.Count; i++)
        {
            for (int j = i + 1; j < shapes.Count; j++)
            {
                double d = MinDistanceBetween(shapes[i], shapes[j]);
                if (d > 0 && d < GapThresholdMm)
                {
                    issues.Add(new VectorDoctorIssue
                    {
                        Kind = VectorDoctorKind.Gap,
                        Title = "Gap between vectors",
                        Severity = VectorDoctorSeverity.Info,
                        ShapeIndices = { i, j }
                    });
                }
            }
        }

        return issues;
    }

    private static bool IsDegenerate(VectorShape shape)
    {
        if (shape.Type == ShapeType.Circle) return shape.Radius <= 0;
        return shape.Points.Count >= 2 && shape.Points[0] == shape.Points[^1] && shape.Points.All(p => p == shape.Points[0]);
    }

    private static bool HasSelfIntersection(VectorShape shape)
    {
        var pts = shape.Points;
        if (pts.Count < 4) return false;
        int n = pts.Count;
        for (int a = 0; a < n; a++)
        {
            var a1 = pts[a];
            var a2 = pts[(a + 1) % n];
            for (int b = a + 2; b < n; b++)
            {
                if (a == 0 && b == n - 1) continue; // adjacent via wrap
                var b1 = pts[b];
                var b2 = pts[(b + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(VectorPoint p1, VectorPoint p2, VectorPoint p3, VectorPoint p4)
    {
        double d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2);
        double d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        bool proper = ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        if (proper) return true;
        return (Math.Abs(d1) < 1e-9 && OnSegment(p3, p4, p1)) || (Math.Abs(d2) < 1e-9 && OnSegment(p3, p4, p2))
            || (Math.Abs(d3) < 1e-9 && OnSegment(p1, p2, p3)) || (Math.Abs(d4) < 1e-9 && OnSegment(p1, p2, p4));
    }

    private static double Cross(VectorPoint a, VectorPoint b, VectorPoint c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(VectorPoint a, VectorPoint b, VectorPoint p)
        => Math.Min(a.X, b.X) - 1e-9 <= p.X && p.X <= Math.Max(a.X, b.X) + 1e-9
        && Math.Min(a.Y, b.Y) - 1e-9 <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;

    private static double MinDistanceBetween(VectorShape s1, VectorShape s2)
    {
        double best = double.MaxValue;
        var p1 = s1.Points;
        var p2 = s2.Points;
        for (int a = 0; a < p1.Count; a++)
        {
            var a1 = p1[a];
            var a2 = p1[(a + 1) % p1.Count];
            for (int b = 0; b < p2.Count; b++)
            {
                var b1 = p2[b];
                var b2 = p2[(b + 1) % p2.Count];
                double d = SegmentsDistance(a1, a2, b1, b2);
                if (d < best) best = d;
                if (best == 0) return 0;
            }
        }
        return best;
    }

    private static double SegmentsDistance(VectorPoint a1, VectorPoint a2, VectorPoint b1, VectorPoint b2)
    {
        if (SegmentsIntersect(a1, a2, b1, b2)) return 0;
        double d = double.MaxValue;
        d = Math.Min(d, PointSegmentDistance(a1, b1, b2));
        d = Math.Min(d, PointSegmentDistance(a2, b1, b2));
        d = Math.Min(d, PointSegmentDistance(b1, a1, a2));
        d = Math.Min(d, PointSegmentDistance(b2, a1, a2));
        return d;
    }

    private static double PointSegmentDistance(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }
}
