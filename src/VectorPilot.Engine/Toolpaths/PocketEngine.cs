using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Pocket toolpath generation (v1: raster-zigzag within shape bounds, with stepdown
/// slices). Contour support via inward offsets is included for circles.
/// </summary>
public static class PocketEngine
{
    /// <param name="shapes">Shapes to pocket.</param>
    /// <param name="cutDepth">Total depth below start (positive).</param>
    /// <param name="stepdown">Max depth per Z slice (positive).</param>
    /// <param name="stepoverPercent">Raster spacing as a percentage of the tool diameter (typical 40–60).</param>
    /// <param name="contourFirst">
    /// Run contour-parallel offset loops before the raster pass. Scanline rastering
    /// alone leaves a stair-stepped wall on curved boundaries; the loops follow the
    /// real outline so a circular pocket finishes as a circle.
    ///
    /// Defaults to TRUE. It previously defaulted to false while PocketParams defaulted
    /// to true, so every caller that skipped the argument — goldens, smoke tests, any
    /// direct engine use — silently got the stair-stepped raster while the UI got
    /// contours. One engine, two behaviours, decided by which overload you happened to
    /// call.
    /// </param>
    public static List<string> Generate(
        ICollection<VectorShape> shapes,
        double cutDepth,
        double stepdown,
        double stepoverPercent,
        double feedRate,
        double plungeRate,
        double spindleSpeed,
        double safeZ,
        double toolDiameter = 0.25,
        bool contourFirst = true)
    {
        var g = new List<string>
        {
            "(VectorPilot pocket toolpath)",
            "G90 ; absolute",
            "G17 ; XY plane",
            "G20 ; inches",
            $"M3 S{spindleSpeed.ToString("F0", CultureInfo.InvariantCulture)} ; spindle on"
        };

        double step = Math.Max(0.01, toolDiameter * stepoverPercent / 100.0);
        double depth = 0;
        while (depth < cutDepth - 1e-9)
        {
            double slice = Math.Min(stepdown > 0 ? stepdown : cutDepth, cutDepth - depth);
            depth += slice;
            foreach (var shape in shapes)
            {
                if (contourFirst && shape.Points.Count >= 3)
                {
                    // Contour loops finish the wall along the true outline, and
                    // ContourPocketEngine walks every offset from the wall inward until
                    // the loops collapse — so for a convex pocket they clear it alone.
                    g.AddRange(ContourPocketEngine.GenerateSlice(
                        shape.Points, -depth, toolDiameter, step, feedRate, plungeRate, safeZ));

                    // The raster then covers whatever the offsets could NOT reach (a
                    // concave outline the inset stops short of). It is clipped to the
                    // outline, so it never leaves the pocket; on a convex shape it is
                    // redundant coverage rather than a correctness problem.
                    //
                    // Suppressing it by predicting "the loops already got everything"
                    // was tried and reverted: the prediction silently skipped the raster
                    // on a small rectangle whose loops do NOT cover it, leaving the
                    // pocket floor uncut. Redundant passes are safe; a missed region is
                    // not.
                }

                GenerateSlice(shape, -depth, step, toolDiameter / 2, feedRate, plungeRate, safeZ, g);
            }
        }

        g.Add("M5 ; spindle off");
        g.Add("M30 ; end");
        return g;
    }

    private static void GenerateSlice(VectorShape shape, double z, double step, double toolRadius, double feedRate, double plungeRate, double safeZ, List<string> g)
    {
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        var b = shape.Bounds();
        if (b.IsEmpty) return;

        // Raster scanlines CLIPPED TO THE SHAPE (was: the bounding box, which cut a
        // rectangle for every outline). Each scanline yields one span per interior
        // interval, so pockets follow the real boundary and islands are skipped.
        double inset = toolRadius;
        double y = b.MinY + inset;
        bool leftToRight = true;
        bool first = true;

        while (y <= b.MaxY - inset + 1e-9)
        {
            var spans = ScanlineSpans(shape, y, inset);
            if (spans.Count == 0) { y += step; continue; }

            if (!leftToRight) spans.Reverse();

            foreach (var (sx, ex) in spans)
            {
                double x0 = leftToRight ? sx : ex;
                double x1 = leftToRight ? ex : sx;
                if (Math.Abs(x1 - x0) < 1e-6) continue;

                if (first)
                {
                    g.Add($"G0 Z{F(safeZ)} ; rapid to safe Z");
                    g.Add($"G0 X{F(x0)} Y{F(y)} ; rapid to start");
                    g.Add($"G1 Z{F(z)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)} ; plunge");
                    first = false;
                }
                else
                {
                    g.Add($"G0 Z{F(safeZ)}");
                    g.Add($"G0 X{F(x0)} Y{F(y)}");
                    g.Add($"G1 Z{F(z)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)}");
                }

                g.Add($"G1 X{F(x1)} Y{F(y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)} ; raster");
            }

            leftToRight = !leftToRight;
            y += step;
        }
        g.Add($"G0 Z{F(safeZ)} ; retract");
    }

    /// <summary>
    /// Interior x-spans where the horizontal line y intersects the shape, inset by
    /// the tool radius. Even-odd crossings; circles handled analytically.
    /// </summary>
    private static List<(double Start, double End)> ScanlineSpans(VectorShape shape, double y, double inset)
    {
        var spans = new List<(double, double)>();

        if (shape.Type == ShapeType.Circle && shape.Points.Count > 0)
        {
            var c = shape.Points[0];
            double r = shape.Radius - inset;
            if (r <= 0) return spans;
            double dy = y - c.Y;
            if (Math.Abs(dy) >= r) return spans;
            double half = Math.Sqrt(r * r - dy * dy);
            spans.Add((c.X - half, c.X + half));
            return spans;
        }

        var pts = shape.Points;
        if (pts.Count < 3) return spans;

        // Clip against the INSET boundary, not the outline with a horizontal fudge.
        //
        // Insetting only in X (xs[i] + inset) is wrong on a curved wall: near the top of
        // a circle the wall recedes in Y as well, so a horizontally-inset span still ends
        // outside the circle by nearly the tool radius. Offsetting the polygon first makes
        // the crossings themselves correct in both axes.
        var boundary = ContourPocketEngine.OffsetInward(pts, inset);
        if (boundary.Count < 3) return spans;

        var xs = new List<double>();
        for (int i = 0; i < boundary.Count; i++)
        {
            var a = boundary[i];
            var b = boundary[(i + 1) % boundary.Count];
            if (Math.Abs(a.Y - b.Y) < 1e-12) continue;             // horizontal edge
            if ((y >= a.Y && y < b.Y) || (y >= b.Y && y < a.Y))    // half-open: no double count at vertices
                xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
        }
        if (xs.Count < 2) return spans;

        xs.Sort();
        for (int i = 0; i + 1 < xs.Count; i += 2)
        {
            double s = xs[i], e = xs[i + 1];
            if (e - s > 1e-6) spans.Add((s, e));
        }
        return spans;
    }
}
