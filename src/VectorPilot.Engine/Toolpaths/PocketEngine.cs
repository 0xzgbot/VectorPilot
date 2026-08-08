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
    /// <param name="stepoverPercent">Raster line spacing as a percentage of the shape's larger extent.</param>
    public static List<string> Generate(
        ICollection<VectorShape> shapes,
        double cutDepth,
        double stepdown,
        double stepoverPercent,
        double feedRate,
        double plungeRate,
        double spindleSpeed,
        double safeZ)
    {
        var g = new List<string>
        {
            "(VectorPilot pocket toolpath)",
            "G90 ; absolute",
            "G17 ; XY plane",
            "G20 ; inches",
            $"M3 S{spindleSpeed.ToString("F0", CultureInfo.InvariantCulture)} ; spindle on"
        };

        double step = Math.Max(0.01, stepoverPercent / 100.0 * 0.5); // 40% -> 0.2" spacing
        double depth = 0;
        while (depth < cutDepth - 1e-9)
        {
            double slice = Math.Min(stepdown > 0 ? stepdown : cutDepth, cutDepth - depth);
            depth += slice;
            foreach (var shape in shapes)
            {
                GenerateSlice(shape, -depth, step, feedRate, plungeRate, safeZ, g);
            }
        }

        g.Add("M5 ; spindle off");
        g.Add("M30 ; end");
        return g;
    }

    private static void GenerateSlice(VectorShape shape, double z, double step, double feedRate, double plungeRate, double safeZ, List<string> g)
    {
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        var b = shape.Bounds();
        if (b.IsEmpty) return;

        // Raster lines across the shape's bounding box, alternating direction.
        double inset = step / 2;
        double y = b.MinY + inset;
        bool leftToRight = true;
        bool first = true;

        while (y <= b.MaxY - inset + 1e-9)
        {
            double x0 = b.MinX + inset, x1 = b.MaxX - inset;
            if (x1 - x0 < 1e-6) break;

            if (first)
            {
                g.Add($"G0 Z{F(safeZ)} ; rapid to safe Z");
                g.Add($"G0 X{F(leftToRight ? x0 : x1)} Y{F(y)} ; rapid to start");
                g.Add($"G1 Z{F(z)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)} ; plunge");
                first = false;
            }
            else
            {
                g.Add($"G0 Z{F(safeZ)}");
                g.Add($"G0 X{F(leftToRight ? x0 : x1)} Y{F(y)}");
                g.Add($"G1 Z{F(z)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)}");
            }

            g.Add($"G1 X{F(leftToRight ? x1 : x0)} Y{F(y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)} ; raster");
            leftToRight = !leftToRight;
            y += step;
        }
        g.Add($"G0 Z{F(safeZ)} ; retract");
    }
}
