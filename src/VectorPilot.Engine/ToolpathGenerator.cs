using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Minimal profile toolpath G-code generator (v1 slice).
/// Absolute coordinates, inch units, ok-wait friendly line-by-line output.
/// Deep engine work (ramps/tabs/leads) comes from the ShopPilot ports in later milestones.
/// </summary>
public static class ToolpathGenerator
{
    public static List<string> GenerateProfile(
        VectorShape shape,
        double cutDepth = 0.25,
        double safeZ = 0.2,
        double feedRate = 100,
        double plungeRate = 50,
        double spindleSpeed = 12000,
        bool clockwise = true,
        double startDepth = 0.0)
    {
        var g = new List<string>
        {
            $"(VectorPilot profile toolpath: {shape.Type})",
            "G90 ; absolute",
            "G17 ; XY plane",
            "G20 ; inches",
            $"M3 S{spindleSpeed.ToString("F0", CultureInfo.InvariantCulture)} ; spindle on"
        };

        var pts = shape.Type switch
        {
            ShapeType.Circle when shape.Points.Count == 1 => GeometryMath.SampleArc(shape.Points[0], shape.Radius, 0, 360),
            ShapeType.Rectangle or ShapeType.Polyline or ShapeType.Line => new List<VectorPoint>(shape.Points),
            _ => new List<VectorPoint>(shape.Points)
        };

        if (pts.Count < 2) return g;

        // Order points for the requested direction (simple reversal; winding-aware in later milestones).
        if (!clockwise && SignedAreaOf(pts) < 0) pts.Reverse();
        else if (clockwise && SignedAreaOf(pts) > 0) pts.Reverse();

        double depth = startDepth + cutDepth;
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

        var first = pts[0];
        g.Add($"G0 Z{safeZ.ToString("F4", CultureInfo.InvariantCulture)} ; rapid to safe Z");
        g.Add($"G0 X{F(first.X)} Y{F(first.Y)} ; rapid to start");
        g.Add($"G1 Z{F(-depth)} F{plungeRate.ToString("F1", CultureInfo.InvariantCulture)} ; plunge");

        for (int i = 1; i < pts.Count; i++)
        {
            var p = pts[i];
            g.Add($"G1 X{F(p.X)} Y{F(p.Y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)}");
        }

        if (shape.Closed && pts.Count > 2)
        {
            g.Add($"G1 X{F(first.X)} Y{F(first.Y)} F{feedRate.ToString("F1", CultureInfo.InvariantCulture)} ; close");
        }

        g.Add($"G0 Z{safeZ.ToString("F4", CultureInfo.InvariantCulture)} ; retract");
        g.Add("M5 ; spindle off");
        g.Add("M30 ; end");
        return g;
    }

    public static List<string> GenerateTestJob(double sizeX = 4.0, double sizeY = 2.0, double depth = 0.25)
    {
        // A simple square profile with a center pocket-ish pass — enough to demo streaming.
        var rect = VectorShape.Rectangle(0, 0, sizeX, sizeY);
        var g = GenerateProfile(rect, depth);
        g.Insert(1, "(Test job: 4x2 rectangle profile)");
        return g;
    }

    private static double SignedAreaOf(List<VectorPoint> pts)
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
}
