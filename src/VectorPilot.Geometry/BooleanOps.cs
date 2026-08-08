namespace VectorPilot.Geometry;

/// <summary>
/// Boolean operations on simple (non-self-intersecting) polygons via Greiner–Hormann
/// clipping. Inputs are normalised to CCW. Union / Subtract / Intersect return result
/// polygons (CCW). Degenerate touch cases are conservatively skipped.
/// </summary>
public static class BooleanOps
{
    private const double Eps = 1e-9;

    private enum Operation { Union, Subtract, Intersect }

    private sealed class Node
    {
        public VectorPoint P;
        public bool IsIntersection;
        public bool IsEntry;   // true when the polygon crosses INTO the other polygon at this node
        public bool Visited;
        public Node? Next;
        public Node? Prev;
        public Node? Other;    // node at the same point in the other ring

        public Node(VectorPoint p, bool isIntersection)
        {
            P = p;
            IsIntersection = isIntersection;
        }
    }

    public static List<List<VectorPoint>> Union(IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b)
        => Clip(a, b, Operation.Union);

    public static List<List<VectorPoint>> Subtract(IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b)
        => Clip(a, b, Operation.Subtract);

    public static List<List<VectorPoint>> Intersect(IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b)
        => Clip(a, b, Operation.Intersect);

    private sealed record EdgeHit(double TA, double TB, VectorPoint P);

    private static List<List<VectorPoint>> Clip(IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b, Operation op)
    {
        var pa = Normalise(a);
        var pb = Normalise(b);
        if (pa.Count < 3 || pb.Count < 3) return new List<List<VectorPoint>>();

        var hits = FindEdgeHits(pa, pb);
        if (hits.Count == 0)
        {
            bool aInB = PointInPolygon(pa[0], pb);
            bool bInA = PointInPolygon(pb[0], pa);
            return op switch
            {
                Operation.Union when aInB || bInA => new List<List<VectorPoint>> { aInB ? pb : pa },
                Operation.Union => new List<List<VectorPoint>> { pa, pb },
                Operation.Intersect => new List<List<VectorPoint>>(),
                Operation.Subtract when aInB => new List<List<VectorPoint>>(), // fully inside → empty (hole unsupported)
                Operation.Subtract => new List<List<VectorPoint>> { pa },
                _ => new List<List<VectorPoint>>()
            };
        }

        var ringA = BuildRing(pa, hits, onB: false);
        var ringB = BuildRing(pb, hits, onB: true);

        // Pair up intersection nodes across rings by point equality.
        var aNodes = RingNodes(ringA).Where(n => n.IsIntersection).ToList();
        var bNodes = RingNodes(ringB).Where(n => n.IsIntersection).ToList();
        foreach (var na in aNodes)
        {
            na.Other = bNodes.FirstOrDefault(nb => nb.P.DistanceTo(na.P) < 1e-6);
        }

        // Classify each A intersection node; B nodes get the complement.
        for (var n = ringA; ; n = n.Next!)
        {
            if (n.IsIntersection)
            {
                n.IsEntry = IsEntry(n, ringA, pb);
                n.Other!.IsEntry = !n.IsEntry;
            }
            if (n.Next == ringA) break;
        }

        var results = new List<List<VectorPoint>>();
        for (var start = ringA; ; start = start.Next!)
        {
            if (!start.IsIntersection || start.Visited) { if (start.Next == ringA) break; continue; }
            bool wantEntry = op == Operation.Intersect;
            if (start.IsEntry != wantEntry) { if (start.Next == ringA) break; continue; }

            var poly = Traverse(start, op);
            if (poly.Count >= 3) results.Add(poly);
            if (start.Next == ringA) break;
        }
        return results;
    }

    /// <summary>Walk one result polygon from the start node until returning to it.</summary>
    private static List<VectorPoint> Traverse(Node start, Operation op)
    {
        var result = new List<VectorPoint>();
        var current = start;
        bool onB = false;
        int guard = 0;

        while (guard++ < 100000)
        {
            MarkVisited(current);
            result.Add(current.P);

            // Advance to the next node on the current ring.
            Node? next;
            if (!current.IsIntersection)
            {
                next = onB ? current.Prev : current.Next;
            }
            else
            {
                // At an intersection: switch rings per the operation rule.
                bool switchNow = op switch
                {
                    Operation.Union => current.IsEntry,      // leaving A's outside (entering B) → jump to B
                    Operation.Intersect => !current.IsEntry, // leaving A's inside (exiting B) → jump to B
                    Operation.Subtract => current.IsEntry,
                    _ => false
                };
                if (switchNow && current.Other is not null)
                {
                    MarkVisited(current.Other);
                    current = current.Other;
                    onB = !onB;
                    result.Add(current.P);
                    if (current == start) break;
                    continue;
                }
                next = onB ? current.Prev : current.Next;
            }

            if (next is null || next == start) break;
            current = next;
        }
        return result;
    }

    private static void MarkVisited(Node n)
    {
        n.Visited = true;
        if (n.Other is not null) n.Other.Visited = true;
    }

    private static List<VectorPoint> Normalise(IReadOnlyList<VectorPoint> pts)
    {
        var list = pts.ToList();
        if (list.Count > 1 && list[0] == list[^1]) list.RemoveAt(list.Count - 1);
        if (SignedArea(list) < 0) list.Reverse();
        return list;
    }

    private static double SignedArea(List<VectorPoint> pts)
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

    private static bool PointInPolygon(VectorPoint p, List<VectorPoint> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static List<EdgeHit> FindEdgeHits(List<VectorPoint> a, List<VectorPoint> b)
    {
        var found = new List<EdgeHit>();
        for (int i = 0; i < a.Count; i++)
        {
            var a1 = a[i];
            var a2 = a[(i + 1) % a.Count];
            for (int j = 0; j < b.Count; j++)
            {
                var b1 = b[j];
                var b2 = b[(j + 1) % b.Count];
                if (SegmentIntersect(a1, a2, b1, b2, out var tA, out var tB, out var p))
                {
                    found.Add(new EdgeHit(tA, tB, p));
                }
            }
        }
        return found;
    }

    private static bool SegmentIntersect(VectorPoint a1, VectorPoint a2, VectorPoint b1, VectorPoint b2,
        out double tA, out double tB, out VectorPoint p)
    {
        tA = tB = 0;
        p = default;
        double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y;
        double d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
        double denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < 1e-12) return false;

        double dx = b1.X - a1.X, dy = b1.Y - a1.Y;
        tA = (dx * d2y - dy * d2x) / denom;
        tB = (dx * d1y - dy * d1x) / denom;

        const double lo = 1e-9, hi = 1 - 1e-9;
        if (tA < lo || tA > hi || tB < lo || tB > hi) return false;

        p = new VectorPoint(a1.X + tA * d1x, a1.Y + tA * d1y);
        return true;
    }

    /// <summary>Build a circular node ring for one polygon, inserting intersection nodes per edge, sorted by t.</summary>
    private static Node BuildRing(List<VectorPoint> poly, List<EdgeHit> hits, bool onB)
    {
        var head = new Node(poly[0], false);
        var tail = head;

        for (int i = 0; i < poly.Count; i++)
        {
            var a1 = poly[i];
            var a2 = poly[(i + 1) % poly.Count];

            // Hits whose parameter belongs to this edge (t is edge-relative for each polygon).
            var edgeHits = hits
                .Where(h => onB ? h.TB > -1e-9 && h.TB < 1 + 1e-9 : h.TA > -1e-9 && h.TA < 1 + 1e-9)
                .Where(h => OnEdge(h.P, a1, a2))
                .OrderBy(h => onB ? h.TB : h.TA)
                .ToList();

            foreach (var hit in edgeHits)
            {
                var node = new Node(hit.P, true);
                tail.Next = node;
                node.Prev = tail;
                tail = node;
            }

            if (i < poly.Count - 1)
            {
                var next = new Node(a2, false);
                tail.Next = next;
                next.Prev = tail;
                tail = next;
            }
        }
        tail.Next = head;
        head.Prev = tail;
        return head;
    }

    private static bool OnEdge(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double cross = (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
        if (Math.Abs(cross) > 1e-6) return false;
        double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
        double lenSq = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        return dot > -1e-9 && dot < lenSq + 1e-9;
    }

    private static IEnumerable<Node> RingNodes(Node head)
    {
        var n = head;
        do
        {
            yield return n;
            n = n.Next!;
        } while (n != head);
    }

    /// <summary>True when the node is an ENTRY for its ring: the edge segment leading into
    /// the node comes from OUTSIDE the other polygon (crossing in).</summary>
    private static bool IsEntry(Node node, Node ring, List<VectorPoint> otherPoly)
    {
        var prev = node.Prev ?? ring;
        var mid = new VectorPoint((prev.P.X + node.P.X) / 2, (prev.P.Y + node.P.Y) / 2);
        return !PointInPolygon(mid, otherPoly);
    }
}
