using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Corner rounding gadget: replaces sharp vertices of a polyline with tangent arcs of
/// a given radius, then emits a cutting program that follows the rounded outline.
///
/// NOT a port of the Mac's GadgetToolpaths.generateRounding — that is an explicit
/// placeholder returning a single segment at (0,0) with the comment "Full
/// implementation would need vector geometry analysis". Porting it verbatim would have
/// imported a stub that looks like a feature.
/// </summary>
public static class RoundingGadget
{
    public sealed class Params
    {
        /// <summary>Corner radius (mm). Zero or negative disables rounding.</summary>
        public double RadiusMm { get; set; } = 6.0;

        /// <summary>Points used to approximate each corner arc.</summary>
        public int SegmentsPerCorner { get; set; } = 8;

        /// <summary>Only round corners sharper than this (degrees). 180 = round everything.</summary>
        public double MaxIncludedAngleDegrees { get; set; } = 170.0;

        public double CutDepthMm { get; set; } = 3.0;
        public double StepDownMm { get; set; } = 2.0;
        public double FeedRateMmPerMin { get; set; } = 1000;
        public double PlungeFeedRateMmPerMin { get; set; } = 300;
        public double SafeZHeightMm { get; set; } = 5.0;
        public double SpindleRpm { get; set; } = 12000;
    }

    public sealed class Result
    {
        public List<string> GcodeLines { get; init; } = new();
        public List<VectorPoint> RoundedOutline { get; init; } = new();
        public int CornersRounded { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Round the corners of a closed polyline. Each corner is trimmed back along both
    /// adjacent edges by the arc's tangent length, then joined by an arc of the
    /// requested radius. A corner whose edges are too short to accept the radius is
    /// trimmed to the largest radius that fits rather than overshooting the geometry.
    /// </summary>
    public static List<VectorPoint> RoundCorners(
        IReadOnlyList<VectorPoint> poly, double radius, int segmentsPerCorner,
        double maxIncludedAngleDegrees, out int cornersRounded)
    {
        cornersRounded = 0;
        int n = poly.Count;
        if (n < 3 || radius <= 0) return poly.ToList();

        int segs = Math.Max(2, segmentsPerCorner);
        double maxAngle = maxIncludedAngleDegrees * Math.PI / 180.0;
        var result = new List<VectorPoint>();

        for (int i = 0; i < n; i++)
        {
            var prev = poly[(i - 1 + n) % n];
            var cur = poly[i];
            var next = poly[(i + 1) % n];

            // Unit vectors pointing away from the corner along each edge.
            var (ux1, uy1, len1) = Unit(cur, prev);
            var (ux2, uy2, len2) = Unit(cur, next);
            if (len1 < 1e-9 || len2 < 1e-9) { result.Add(cur); continue; }

            // Included angle at the corner.
            double dot = Math.Clamp(ux1 * ux2 + uy1 * uy2, -1.0, 1.0);
            double included = Math.Acos(dot);

            // A near-straight vertex has nothing to round.
            if (included >= maxAngle || included < 1e-6) { result.Add(cur); continue; }

            // Tangent length from the corner to each arc endpoint.
            double half = included / 2.0;
            double tanLen = radius / Math.Tan(half);

            // Never eat more than half of either edge, or adjacent corners collide.
            double usable = Math.Min(tanLen, Math.Min(len1, len2) / 2.0);
            if (usable <= 1e-9) { result.Add(cur); continue; }

            double effRadius = usable * Math.Tan(half);

            var a = new VectorPoint(cur.X + ux1 * usable, cur.Y + uy1 * usable);
            var b = new VectorPoint(cur.X + ux2 * usable, cur.Y + uy2 * usable);

            // Arc centre lies along the bisector, at distance r / sin(half).
            double bx = ux1 + ux2, by = uy1 + uy2;
            double blen = Math.Sqrt(bx * bx + by * by);
            if (blen < 1e-9) { result.Add(cur); continue; }
            bx /= blen; by /= blen;

            double centreDist = effRadius / Math.Sin(half);
            var centre = new VectorPoint(cur.X + bx * centreDist, cur.Y + by * centreDist);

            double startAng = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            double endAng = Math.Atan2(b.Y - centre.Y, b.X - centre.X);

            // Sweep the short way around.
            double sweep = endAng - startAng;
            while (sweep > Math.PI) sweep -= 2 * Math.PI;
            while (sweep < -Math.PI) sweep += 2 * Math.PI;

            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double ang = startAng + sweep * t;
                result.Add(new VectorPoint(
                    centre.X + Math.Cos(ang) * effRadius,
                    centre.Y + Math.Sin(ang) * effRadius));
            }
            cornersRounded++;
        }

        return result;
    }

    private static (double X, double Y, double Len) Unit(VectorPoint from, VectorPoint to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-12 ? (0, 0, 0) : (dx / len, dy / len, len);
    }

    public static Result Compute(IReadOnlyList<VectorShape> shapes, Params p)
    {
        if (shapes.Count == 0)
            return new Result { Error = "Corner Rounding needs a closed shape — select one first." };

        if (p.RadiusMm <= 0)
            return new Result { Error = "Corner radius must be greater than zero." };

        string F(double v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        var g = new List<string>
        {
            "(VectorPilot corner rounding gadget)",
            "G90", "G17", "G21",
            $"M3 S{p.SpindleRpm:F0}"
        };

        var lastOutline = new List<VectorPoint>();
        int totalCorners = 0;
        bool anyGeometry = false;

        foreach (var shape in shapes.Where(s => s.Points.Count >= 3))
        {
            var rounded = RoundCorners(shape.Points, p.RadiusMm, p.SegmentsPerCorner,
                                       p.MaxIncludedAngleDegrees, out int corners);
            if (rounded.Count < 2) continue;

            totalCorners += corners;
            lastOutline = rounded;
            anyGeometry = true;

            double depth = 0;
            while (depth < p.CutDepthMm - 1e-9)
            {
                double slice = Math.Min(p.StepDownMm > 0 ? p.StepDownMm : p.CutDepthMm, p.CutDepthMm - depth);
                depth += slice;
                double z = -depth;

                g.Add($"G0 Z{F(p.SafeZHeightMm)}");
                g.Add($"G0 X{F(rounded[0].X)} Y{F(rounded[0].Y)}");
                g.Add($"G1 Z{F(z)} F{(int)p.PlungeFeedRateMmPerMin}");

                for (int i = 1; i < rounded.Count; i++)
                    g.Add($"G1 X{F(rounded[i].X)} Y{F(rounded[i].Y)} F{(int)p.FeedRateMmPerMin}");

                // Close the outline.
                g.Add($"G1 X{F(rounded[0].X)} Y{F(rounded[0].Y)} F{(int)p.FeedRateMmPerMin}");
            }
        }

        if (!anyGeometry)
            return new Result { Error = "Corner Rounding needs a closed shape with at least 3 points." };

        g.Add($"G0 Z{F(p.SafeZHeightMm)}");
        g.Add("M5");

        return new Result { GcodeLines = g, RoundedOutline = lastOutline, CornersRounded = totalCorners };
    }
}
