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
                // P-201: generate the offset loops ONCE, reuse them for both the
                // contour pass and the remainder raster. The loops walk inward until
                // they collapse, so the only region they cannot reach is the interior
                // of the INNERMOST loop — the raster is clipped to exactly that, not
                // to the whole outline. On a convex pocket this removes nearly all
                // double-cutting; on any pocket it keeps the floor fully covered,
                // because the raster still sweeps everything inside the last loop.
                //
                // History: suppressing the raster entirely was tried and reverted —
                // the prediction silently skipped floors on a small rectangle whose
                // loops do NOT cover them. Clipping to the innermost loop keeps the
                // guarantee structurally: whatever the loops missed is inside that
                // loop, and the raster crosses all of it.
                List<ContourPocketEngine.Loop>? loops = null;
                if (contourFirst)
                    loops = LoopSource(shape, toolDiameter, step);

                if (loops is { Count: > 0 })
                {
                    EmitLoops(loops, -depth, feedRate, plungeRate, safeZ, g);
                    RemainderRaster(loops[^1].Points, -depth, step, feedRate, plungeRate, safeZ, g);
                }
                else
                {
                    // Pocket too small for even one loop (or raster-only mode): the
                    // clipped raster is the only coverage there is.
                    GenerateSlice(shape, -depth, step, toolDiameter / 2, feedRate, plungeRate, safeZ, g);
                }
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

    // ---- P-201: leftover-only raster ----

    /// <summary>Boundary polygon for loop generation: analytic circles become dense
    /// polylines so the offset machinery can work on them like any other outline.</summary>
    private static List<VectorPoint> LoopBoundary(VectorShape shape)
    {
        if (shape.Type == ShapeType.Circle && shape.Points.Count > 0 && shape.Radius > 0)
        {
            var c = shape.Points[0];
            const int segments = 64;
            var pts = new List<VectorPoint>(segments);
            for (int i = 0; i < segments; i++)
            {
                double t = 2 * Math.PI * i / segments;
                pts.Add(new VectorPoint(c.X + shape.Radius * Math.Cos(t), c.Y + shape.Radius * Math.Sin(t)));
            }
            return pts;
        }
        return shape.Points.ToList();
    }

    private static List<ContourPocketEngine.Loop> LoopSource(VectorShape shape, double toolDiameter, double step)
        => ContourPocketEngine.GenerateLoops(LoopBoundary(shape), toolDiameter, step);


    /// <summary>
    /// Emit the contour G-code for pre-computed loops (same output shape as
    /// GenerateSlice, but without regenerating them).
    /// </summary>
    private static void EmitLoops(List<ContourPocketEngine.Loop> loops, double z, double feedRate, double plungeRate, double safeZ, List<string> g)
    {
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        bool first = true;
        foreach (var loop in loops)
        {
            var start = loop.Points[0];
            g.Add(first
                ? $"G0 Z{F(safeZ)} ; rapid to safe Z"
                : $"G0 Z{F(safeZ)}");
            g.Add($"G0 X{F(start.X)} Y{F(start.Y)}");
            g.Add($"G1 Z{F(z)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)}");
            first = false;

            for (int i = 1; i < loop.Points.Count; i++)
                g.Add($"G1 X{F(loop.Points[i].X)} Y{F(loop.Points[i].Y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)}");
            g.Add($"G1 X{F(start.X)} Y{F(start.Y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)}");
        }
        g.Add($"G0 Z{F(safeZ)}");
    }

    /// <summary>
    /// P-201 remainder raster: scanlines clipped to the INNERMOST contour loop's
    /// polygon instead of the whole outline. Everything outside that loop was
    /// already machined by the loops themselves; everything inside it is guaranteed
    /// coverage. Zigzag direction preserved; spans shorter than a step are skipped.
    /// </summary>
    private static void RemainderRaster(
        List<VectorPoint> innermostLoop,
        double z, double step, double feedRate, double plungeRate, double safeZ, List<string> g)
    {
        if (innermostLoop.Count < 3) return;

        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        var b = Bounds(innermostLoop);

        double y = b.MinY;
        bool leftToRight = true;
        bool first = true;

        while (y <= b.MaxY + 1e-9)
        {
            // Clip against the loop polygon itself (no extra inset: the loop is
            // already one tool radius inside the wall).
            var xs = new List<double>();
            int n = innermostLoop.Count;
            for (int i = 0; i < n; i++)
            {
                var a = innermostLoop[i];
                var c = innermostLoop[(i + 1) % n];
                if (Math.Abs(a.Y - c.Y) < 1e-12) continue;
                if ((y >= a.Y && y < c.Y) || (y >= c.Y && y < a.Y))
                    xs.Add(a.X + (y - a.Y) / (c.Y - a.Y) * (c.X - a.X));
            }
            if (xs.Count >= 2)
            {
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    double sx = xs[i], ex = xs[i + 1];
                    if (ex - sx < 1e-6) continue;

                    double x0 = leftToRight ? sx : ex;
                    double x1 = leftToRight ? ex : sx;

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

                    g.Add($"G1 X{F(x1)} Y{F(y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)} ; remainder raster");
                }
            }

            leftToRight = !leftToRight;
            y += step;
        }
        g.Add($"G0 Z{F(safeZ)} ; retract");
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<VectorPoint> poly)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in poly)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (minX, minY, maxX, maxY);
    }
}
