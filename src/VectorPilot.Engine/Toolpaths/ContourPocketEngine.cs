using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Contour-parallel pocket clearing: successive inward offsets of the boundary,
/// then a raster pass over whatever the loops could not reach.
///
/// Why this exists: the shipped pocket rasters scanlines clipped to the outline.
/// That is correct in extent but not the same as an offset pocket — it leaves a
/// stair-stepped wall on curved boundaries and crosses the interior far more than a
/// contour pass. The Mac's "spiralOut" is circular rings struck from the bounding-box
/// centre, which ignores the outline entirely, so it is not the thing to port.
///
/// The loops here follow the real boundary, so a circular island is cleared by
/// concentric circles and no move leaves the outline.
/// </summary>
public static class ContourPocketEngine
{
    /// <summary>One closed clearing loop at a given inset distance.</summary>
    public sealed class Loop
    {
        public double Inset { get; init; }
        public List<VectorPoint> Points { get; init; } = new();
    }

    /// <summary>
    /// Inward offset loops, starting one tool radius inside the boundary and stepping
    /// by <paramref name="stepover"/> until the offset collapses.
    /// </summary>
    public static List<Loop> GenerateLoops(
        IReadOnlyList<VectorPoint> boundary,
        double toolDiameter,
        double stepover,
        int maxLoops = 200)
    {
        var loops = new List<Loop>();
        if (boundary.Count < 3 || toolDiameter <= 0 || stepover <= 0) return loops;

        double inset = toolDiameter / 2.0;
        var source = boundary.ToList();
        double sourceArea = Math.Abs(SignedArea(source));

        for (int i = 0; i < maxLoops; i++)
        {
            var offset = OffsetInward(source, inset);
            if (offset.Count < 3) break;                 // collapsed

            double area = Math.Abs(SignedArea(offset));
            if (area <= 1e-9) break;

            // An inset polygon must be strictly smaller than its source. A 4mm square
            // inset by 3mm folds through itself and comes back as a valid-looking 2mm
            // square — checking the winding sign alone misses it, because reversal
            // flips the sign twice.
            if (area >= sourceArea) break;

            // The offset must also still fit the tool: a loop narrower than the cutter
            // cannot be cut.
            if (!FitsTool(offset, toolDiameter)) break;

            loops.Add(new Loop { Inset = inset, Points = offset });
            inset += stepover;
        }
        return loops;
    }

    /// <summary>
    /// Can a tool of this diameter actually follow the loop? Approximated by the
    /// loop's smaller bounding extent, which is enough to reject sub-tool pockets.
    /// </summary>
    private static bool FitsTool(IReadOnlyList<VectorPoint> poly, double toolDiameter)
    {
        double w = poly.Max(p => p.X) - poly.Min(p => p.X);
        double h = poly.Max(p => p.Y) - poly.Min(p => p.Y);
        return Math.Min(w, h) >= toolDiameter;
    }

    /// <summary>
    /// Offset a closed polygon inward by <paramref name="distance"/>. Vertices move
    /// along the angle bisector of their adjacent edges, which keeps the loop parallel
    /// to the original boundary rather than to its bounding box.
    /// </summary>
    public static List<VectorPoint> OffsetInward(IReadOnlyList<VectorPoint> poly, double distance)
    {
        var result = new List<VectorPoint>();
        int n = poly.Count;
        if (n < 3) return result;

        // Inward is the side the polygon's winding points to.
        double sign = SignedArea(poly) > 0 ? 1.0 : -1.0;
        var centroid = Centroid(poly);

        for (int i = 0; i < n; i++)
        {
            var prev = poly[(i - 1 + n) % n];
            var cur = poly[i];
            var next = poly[(i + 1) % n];

            // Inward normals of the two edges meeting at `cur`.
            var n1 = InwardNormal(prev, cur, sign);
            var n2 = InwardNormal(cur, next, sign);

            double bx = n1.X + n2.X, by = n1.Y + n2.Y;
            double len = Math.Sqrt(bx * bx + by * by);
            if (len < 1e-9) continue;                    // reversing spike

            bx /= len; by /= len;

            // Scale so the offset edge sits `distance` from the original, not the
            // vertex `distance` from the corner (a sharp corner needs more travel).
            double cosHalf = (n1.X * bx + n1.Y * by);
            double scale = cosHalf > 1e-6 ? distance / cosHalf : distance;
            scale = Math.Min(scale, distance * 10);      // clamp needle corners

            result.Add(new VectorPoint(cur.X + bx * scale, cur.Y + by * scale));
        }

        // Drop points that crossed to the far side of the shape.
        return result.Where(p => PointInPolygon(p, poly)).ToList();
    }

    private static VectorPoint InwardNormal(VectorPoint a, VectorPoint b, double sign)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) return new VectorPoint(0, 0);
        // Left normal for CCW winding, right for CW.
        return new VectorPoint(-dy / len * sign, dx / len * sign);
    }

    public static double SignedArea(IReadOnlyList<VectorPoint> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2.0;
    }

    public static VectorPoint Centroid(IReadOnlyList<VectorPoint> poly)
        => new(poly.Average(p => p.X), poly.Average(p => p.Y));

    public static bool PointInPolygon(VectorPoint p, IReadOnlyList<VectorPoint> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            if ((yi > p.Y) != (yj > p.Y) &&
                p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// G-code for one depth: contour loops from the outside in, each closed, at the
    /// requested Z. Emits nothing when the pocket is smaller than the tool.
    /// </summary>
    public static List<string> GenerateSlice(
        IReadOnlyList<VectorPoint> boundary,
        double z,
        double toolDiameter,
        double stepover,
        double feedRate,
        double plungeRate,
        double safeZ)
    {
        var g = new List<string>();
        var loops = GenerateLoops(boundary, toolDiameter, stepover);
        if (loops.Count == 0) return g;

        string F(double v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var loop in loops)
        {
            var start = loop.Points[0];
            g.Add($"G0 Z{F(safeZ)}");
            g.Add($"G0 X{F(start.X)} Y{F(start.Y)}");
            g.Add($"G1 Z{F(z)} F{(int)plungeRate}");

            for (int i = 1; i < loop.Points.Count; i++)
            {
                var p = loop.Points[i];
                g.Add($"G1 X{F(p.X)} Y{F(p.Y)} F{(int)feedRate}");
            }
            // Close the ring so no seam is left uncut.
            g.Add($"G1 X{F(start.X)} Y{F(start.Y)} F{(int)feedRate}");
        }

        g.Add($"G0 Z{F(safeZ)}");
        return g;
    }
}
