using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Rotary axis mode (ported from Swift RotaryMode).</summary>
public enum RotaryMode
{
    Engrave,
    Cylinder,
    Sphere,
    Custom
}

/// <summary>Rotary wrap direction (ported from Swift RotaryDirection).</summary>
public enum RotaryDirection
{
    Clockwise,
    CounterClockwise
}

/// <summary>
/// Rotary configuration (ported from Swift RotaryConfig): the linear→angular mapping used by
/// RotaryEngine.linearToAngular — angle = (linearPosition / circumference) × 360, wrapped to 0..360.
/// </summary>
public sealed class RotaryConfig
{
    public RotaryMode Mode { get; set; } = RotaryMode.Cylinder;
    public double Diameter { get; set; } = 50.0;
    public double AxisLength { get; set; } = 100.0;
    public RotaryDirection Direction { get; set; } = RotaryDirection.Clockwise;
    public double ZeroAngle { get; set; }
    public double StartAngle { get; set; }
    public double EndAngle { get; set; } = 360.0;
    public bool WrapEnabled { get; set; } = true;
    public double WrapOverlap { get; set; } = 5.0;
    public double Tension { get; set; } = 0.5;

    public RotaryConfig() { }

    public RotaryConfig(RotaryMode mode, double diameter, double axisLength = 100.0,
        RotaryDirection direction = RotaryDirection.Clockwise, bool wrapEnabled = true)
    {
        Mode = mode;
        Diameter = Math.Max(1.0, diameter);
        AxisLength = Math.Max(1.0, axisLength);
        Direction = direction;
        WrapEnabled = wrapEnabled;
    }
}

/// <summary>
/// Rotary wrap parameters (ported from Swift RotaryWrapToolpathParams).
/// </summary>
public sealed class RotaryWrapParams
{
    public double DiameterMm { get; set; } = 50.0;
    public double CutDepthMm { get; set; } = 1.0;
    public RotaryDirection Direction { get; set; } = RotaryDirection.Clockwise;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1200;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

/// <summary>
/// Computed specialty toolpath result (ported from Swift SpecialtyResult).
/// </summary>
public sealed class SpecialtyResult
{
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int FeatureCount { get; init; }
}

/// <summary>
/// Rotary wrap engine (ported verbatim from Swift RotaryWrapToolpathEngine, SPK-0904 lean slice).
/// Wrap 2D vectors around a rotary axis: each vector's X dimension maps to A-axis rotation
/// (degrees, direction-aware, wrapped 0..360), Y stays the axis dimension. Emits real A-axis
/// G-code:
///   % / O=ROTARY_WRAP_TOOLPATH / header / M3 S.. /
///   per path: G0 A.. Y.., G0 Z.., G1 Z.. F.., G1 A.. Y.. F.., G0 Z.. / M30 / %
/// Estimated time = unwrapped 2D path length / feed × 60 + featureCount × 1.2 seconds.
/// All numbers formatted with the invariant culture (%.1f / %.2f / %.3f as in the Swift).
/// </summary>
public static class RotaryWrapEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, RotaryWrapParams params_,
        double stockHeightMm = 25.0)
    {
        var gcode = new List<string> { "%", "O=ROTARY_WRAP_TOOLPATH" };
        gcode.Add($"(Rotary wrap: Ø {F1(params_.DiameterMm)}mm · depth {F2(params_.CutDepthMm)}mm · " +
                  $"{(params_.Direction == RotaryDirection.Clockwise ? "CW" : "CCW")})");
        if (params_.SpindleRpm > 0)
        {
            gcode.Add($"M3 S{(int)params_.SpindleRpm}");
        }

        var config = new RotaryConfig(
            mode: RotaryMode.Cylinder,
            diameter: params_.DiameterMm,
            axisLength: 0,
            direction: params_.Direction,
            wrapEnabled: true);

        double z = -params_.CutDepthMm;
        int featureCount = 0;
        double totalLength = 0;

        // X (flat unwrap mm) → A (degrees, direction-aware).
        double AngleForX(double x)
        {
            double a = LinearToAngular(x, config);
            return params_.Direction == RotaryDirection.Clockwise ? a : PositiveModulo(360.0 - a, 360.0);
        }

        foreach (var path in paths)
        {
            if (path.Points.Count < 2) continue;
            featureCount += 1;
            var first = path.Points[0];
            gcode.Add("");
            gcode.Add($"(Wrapped path {featureCount})");
            gcode.Add($"G0 A{F3(AngleForX(first.X))} Y{F3(first.Y)}");
            gcode.Add($"G0 Z{F3(params_.SafeZHeightMm)}");
            gcode.Add($"G1 Z{F3(z)} F{(int)params_.PlungeRateMmPerMin}");
            for (int i = 1; i < path.Points.Count; i++)
            {
                var p = path.Points[i];
                gcode.Add($"G1 A{F3(AngleForX(p.X))} Y{F3(p.Y)} F{(int)params_.FeedRateMmPerMin}");
            }
            // Real length: unwrapped 2D distance between consecutive points.
            for (int i = 1; i < path.Points.Count; i++)
            {
                totalLength += path.Points[i - 1].DistanceTo(path.Points[i]);
            }
            gcode.Add($"G0 Z{F3(params_.SafeZHeightMm)}");
        }

        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");

        double time = totalLength / Math.Max(params_.FeedRateMmPerMin, 1) * 60.0 + featureCount * 1.2;
        return new SpecialtyResult
        {
            GcodeLines = gcode,
            EstimatedTimeSeconds = time,
            FeatureCount = featureCount
        };
    }

    /// <summary>Converts linear to angular position (ported from Swift RotaryEngine.linearToAngular).</summary>
    public static double LinearToAngular(double linearPosition, RotaryConfig config)
    {
        double circumference = Circumference(config);
        double angle = (linearPosition / circumference) * 360.0;
        return PositiveModulo(angle, 360.0);
    }

    /// <summary>Converts angular to linear position (ported from Swift RotaryEngine.angularToLinear).</summary>
    public static double AngularToLinear(double angle, RotaryConfig config)
    {
        double circumference = Circumference(config);
        return (angle / 360.0) * circumference;
    }

    /// <summary>Circumference of the rotary stock (ported from Swift RotaryEngine.circumference).</summary>
    public static double Circumference(RotaryConfig config) => Math.PI * config.Diameter;

    private static double PositiveModulo(double value, double divisor)
    {
        var r = value % divisor;
        if (r < 0) r += divisor;
        return r;
    }

    private static string F3(double v)
        => (Math.Abs(v) < 1e-9 ? 0.0 : v).ToString("0.000", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
