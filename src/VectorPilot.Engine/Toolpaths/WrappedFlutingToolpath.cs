using VectorPilot.Geometry;

namespace VectorPilot.Engine;

public enum WrapDirection { Clockwise, CounterClockwise }

/// <summary>Wrapped-fluting params (ported from WrappedFlutingParams.swift, H04).</summary>
public sealed class WrappedFlutingParams
{
    public double StartDepthMm { get; set; }
    public double CutDepthMm { get; set; } = 4.0;
    public double PassDepthMm { get; set; } = 2.0; // 0 = single pass
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double ToolDiameterMm { get; set; } = 6.0;
    public double SpindleRpm { get; set; }
    public double WrapDiameterMm { get; set; } = 50.0;
    public WrapDirection Direction { get; set; } = WrapDirection.Clockwise;

    public static WrappedFlutingParams FromMaterial(Material material) => new()
    {
        CutDepthMm = Math.Max(0.5, material.MaxDepthOfCutMm) * 2.0,
        PassDepthMm = Math.Max(0.5, material.MaxDepthOfCutMm),
        FeedRateMmPerMin = material.MaxFeedRateMmPerMin * 0.7,
        PlungeRateMmPerMin = material.MaxFeedRateMmPerMin * 0.3,
        WrapDiameterMm = 50.0
    };
}

public sealed class WrappedFlutingResult
{
    public List<string> Gcode { get; init; } = new();
    public int MoveCount { get; init; }
    public string Marker { get; init; } = "O=WRAPPED_FLUTING";
}

/// <summary>
/// Fluting wrapped onto a rotary cylinder (ported from
/// WrappedFlutingToolpathEngine.swift, H04): X stays axial, flat Y wraps to
/// A degrees about the cylinder axis: a = y / (π·d) · 360.
/// </summary>
public static class WrappedFlutingToolpathEngine
{
    public static WrappedFlutingResult Compute(IReadOnlyList<VectorPoint> points, WrappedFlutingParams p)
    {
        var gcode = new List<string> { "O=WRAPPED_FLUTING" };
        gcode.Add($"(Wrapped fluting: Ø {p.WrapDiameterMm:0.0}mm · depth {p.CutDepthMm:0.00}mm · {(p.Direction == WrapDirection.Clockwise ? "CW" : "CCW")})");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        int passes = p.PassDepthMm > 0 ? Math.Max(1, (int)Math.Ceiling(p.CutDepthMm / p.PassDepthMm)) : 1;
        int moveCount = 0;
        double circumference = Math.PI * p.WrapDiameterMm;

        double Angle(double y)
        {
            double a = ((y / circumference * 360.0) % 360.0 + 360.0) % 360.0;
            return p.Direction == WrapDirection.Clockwise ? a : (360.0 - a) % 360.0;
        }

        if (points.Count < 2)
        {
            gcode.Add("");
            gcode.Add("M30");
            return new WrappedFlutingResult { Gcode = gcode, MoveCount = 0 };
        }

        var first = points[0];
        for (int pass = 1; pass <= passes; pass++)
        {
            double depth = p.PassDepthMm > 0 ? Math.Min(pass * p.PassDepthMm, p.CutDepthMm) : p.CutDepthMm;
            double z = -(p.StartDepthMm + depth);
            gcode.Add("");
            gcode.Add($"(Wrapped flute pass {pass}/{passes})");
            gcode.Add($"G0 X{first.X:0.000} A{Angle(first.Y):0.000}");
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
            moveCount++;
            foreach (var pt in points.Skip(1))
            {
                gcode.Add($"G1 X{pt.X:0.000} A{Angle(pt.Y):0.000} F{(int)p.FeedRateMmPerMin}");
                moveCount++;
            }
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
        }
        gcode.Add("");
        gcode.Add("M30");
        return new WrappedFlutingResult { Gcode = gcode, MoveCount = moveCount };
    }
}
