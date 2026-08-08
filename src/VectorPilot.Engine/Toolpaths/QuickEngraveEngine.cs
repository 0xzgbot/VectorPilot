using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Quick engrave params (ported from QuickEngraveParams.swift).</summary>
public sealed class QuickEngraveParams
{
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double DepthMm { get; set; } = 1.0;
    public double LeadInDistanceMm { get; set; } = 5.0;
    public double LeadOutDistanceMm { get; set; } = 5.0;
    /// <summary>Per-vector engraving depths (Guid → max Z depth in mm).</summary>
    public Dictionary<Guid, double> VectorDepths { get; set; } = new();

    public double HalfAngleRadians => Math.PI / 180.0 * VBitAngleDegrees / 2.0;
    public double TipWidthAtDepth(double depth) => Math.Abs(depth) * Math.Tan(HalfAngleRadians);
}

public sealed class QuickEngraveResult
{
    public QuickEngraveParams Params { get; init; }
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PassCount { get; init; } = 1;
}

/// <summary>
/// Single-pass quick engrave (ported from QuickEngraveEngine.swift): one pass
/// per vector at a constant depth, with per-vector depth overrides.
/// </summary>
public static class QuickEngraveEngine
{
    public static QuickEngraveResult Compute(IReadOnlyList<VectorShape> vectors, QuickEngraveParams p)
    {
        var gcode = new List<string>
        {
            "%",
            "O=QUICK_ENGRAVE_TOOLPATH",
            $"(V-Bit: {(int)p.VBitAngleDegrees}°)",
            "(Quick Engrave — single pass)"
        };
        double feed = p.FeedRateMmPerMin;
        double plungeFeed = p.PlungeFeedRateMmPerMin;
        double totalLength = 0;

        foreach (var vector in vectors)
        {
            if (vector.Points.Count < 2) continue;
            double depth = p.VectorDepths.TryGetValue(vector.Id, out var d) ? d : p.DepthMm;
            double zDepth = -depth;

            gcode.Add("");
            gcode.Add($"(Quick Engrave, Z={zDepth:0.000})");
            gcode.Add("G0 Z5.0");

            var start = vector.Points[0];
            double leadInX = start.X - p.LeadInDistanceMm;
            gcode.Add($"G0 X{leadInX:0.000} Y{start.Y:0.000}");
            gcode.Add($"G1 Z{zDepth:0.000} F{(int)plungeFeed}");
            gcode.Add($"G1 X{start.X:0.000} Y{start.Y:0.000} F{(int)feed}");

            for (int i = 1; i < vector.Points.Count; i++)
            {
                var pt = vector.Points[i];
                gcode.Add($"G1 X{pt.X:0.000} Y{pt.Y:0.000} Z{zDepth:0.000} F{(int)feed}");
            }
            if (vector.Closed && vector.Points.Count > 2)
            {
                gcode.Add($"G1 X{start.X:0.000} Y{start.Y:0.000} Z{zDepth:0.000} F{(int)feed}");
            }

            var end = vector.Points[^1];
            double leadOutX = end.X + p.LeadOutDistanceMm;
            gcode.Add($"G1 X{leadOutX:0.000} Y{end.Y:0.000} Z{zDepth:0.000} F{(int)feed}");
            gcode.Add("G0 Z5.0");

            totalLength += PathLength(vector);
        }

        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");

        return new QuickEngraveResult
        {
            Params = p,
            GcodeLines = gcode,
            EstimatedTimeSeconds = totalLength / feed * 60.0,
            PassCount = 1
        };
    }

    private static double PathLength(VectorShape shape)
    {
        double len = 0;
        for (int i = 1; i < shape.Points.Count; i++) len += shape.Points[i - 1].DistanceTo(shape.Points[i]);
        if (shape.Closed && shape.Points.Count > 2) len += shape.Points[^1].DistanceTo(shape.Points[0]);
        return len;
    }
}
