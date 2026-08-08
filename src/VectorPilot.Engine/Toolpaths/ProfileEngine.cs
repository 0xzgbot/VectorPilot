using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

public enum ProfileCutMode { OutCut, InCut, OnCut }
public enum ProfileRampType { None, Smooth, ZigZag, Spiral }
public enum ProfileLeadType { None, StraightLine, CircularArc }
public enum ProfileCutDirection { Climb, Conventional }

/// <summary>Profile toolpath params (ported from ProfileToolpathParams.swift, SPK-1136a key set).</summary>
public sealed class ProfileToolpathParams
{
    public ProfileCutMode CutMode { get; set; } = ProfileCutMode.OnCut;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double MaxDepthOfCutMm { get; set; } = 2.0;
    public double ToolDiameterMm { get; set; } = 6.0;
    public List<double> TabWidths { get; set; } = new();
    public int FinishPasses { get; set; } = 1;
    public double LeadInDistanceMm { get; set; } = 5.0;
    public double LeadOutDistanceMm { get; set; } = 5.0;
    public double SpindleRpm { get; set; } = 0;
    public bool AddTabs { get; set; }
    public double TabLengthMm { get; set; } = 6.0;
    public double TabThicknessMm { get; set; } = 3.0;
    public double TabSpacingMm { get; set; } = 25.0;
    public bool Use3DTabs { get; set; }
    public ProfileRampType RampType { get; set; } = ProfileRampType.Smooth;
    public double RampDistanceMm { get; set; } = 3.0;
    public ProfileLeadType LeadInType { get; set; } = ProfileLeadType.None;
    public double LeadInAngleDegrees { get; set; } = 45.0;
    public double CircularLeadRadiusMm { get; set; } = 2.0;
    public bool DoLeadOut { get; set; }
    public bool SharpExternalCorner { get; set; }
    public bool SharpInternalCorner { get; set; }
    public ProfileCutDirection CutDirection { get; set; } = ProfileCutDirection.Climb;

    public static ProfileToolpathParams FromMaterial(Material material, double toolDiameterMm)
        => new()
        {
            FeedRateMmPerMin = material.MaxFeedRateMmPerMin * 0.7,
            PlungeFeedRateMmPerMin = material.MaxFeedRateMmPerMin * 0.3,
            MaxDepthOfCutMm = Math.Min(material.MaxDepthOfCutMm, toolDiameterMm),
            ToolDiameterMm = toolDiameterMm,
            LeadInDistanceMm = toolDiameterMm * 2,
            LeadOutDistanceMm = toolDiameterMm * 2
        };
}

public sealed class ProfileToolpathResult
{
    public ProfileToolpathParams Params { get; init; } = new();
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PassCount { get; init; }
    public List<string> Path { get; init; } = new();
    public bool HasTabs => Params.TabWidths.Count > 0;
}

/// <summary>Profile engine (ported from ProfileToolpathEngine.swift compute()).</summary>
public static class ProfileToolpathEngine
{
    public static ProfileToolpathResult Compute(
        IReadOnlyList<VectorShape> vectors,
        ProfileToolpathParams params_,
        double stockHeightMm = 25.0)
    {
        var gcode = new List<string>();
        var path = new List<string>();
        double feed = params_.FeedRateMmPerMin;
        double plungeFeed = params_.PlungeFeedRateMmPerMin;
        double toolRadius = params_.ToolDiameterMm / 2.0;

        gcode.Add("%");
        gcode.Add("O=PROFILE_TOOLPATH");
        gcode.Add($"(Tool: {params_.ToolDiameterMm * 10:0}mm)");
        if (params_.SpindleRpm > 0) gcode.Add($"M3 S{params_.SpindleRpm:0}");

        double totalLength = 0;
        int maxPassCount = 0;

        foreach (var vector in vectors)
        {
            if (vector.Points.Count == 0) continue;

            double offsetDistance = params_.CutMode switch
            {
                ProfileCutMode.OutCut => toolRadius,
                ProfileCutMode.InCut => -toolRadius,
                _ => 0
            };

            List<VectorPoint> offsetPoints;
            if (vector.Closed && vector.Points.Count >= 3)
            {
                var result = VectorOffset.OffsetClosedPolyline(vector.Points, offsetDistance);
                offsetPoints = result?.OffsetPath ?? vector.Points.Select(p => new VectorPoint(p.X + offsetDistance, p.Y)).ToList();
            }
            else
            {
                offsetPoints = vector.Points.Select(p => new VectorPoint(p.X + offsetDistance, p.Y + offsetDistance)).ToList();
            }

            int passCount = (int)Math.Ceiling(stockHeightMm / params_.MaxDepthOfCutMm);
            maxPassCount = Math.Max(maxPassCount, passCount);

            for (int pass = 1; pass <= passCount; pass++)
            {
                double zDepth = -pass * params_.MaxDepthOfCutMm;
                gcode.Add("");
                gcode.Add($"(Pass {pass}/{passCount}, Z={zDepth:0.000})");
                gcode.Add("G0 Z5.0");

                if (offsetPoints.Count > 0)
                {
                    var start = offsetPoints[0];
                    double leadInX = start.X - params_.LeadInDistanceMm;
                    gcode.Add($"G0 X{leadInX:0.000} Y{start.Y:0.000}");
                    gcode.Add($"G1 Z{zDepth:0.000} F{(int)plungeFeed}");
                    gcode.Add($"G1 X{start.X:0.000} Y{start.Y:0.000} F{(int)feed}");
                    path.Add($"G1 X{start.X:0.000} Y{start.Y:0.000} F{(int)feed}");
                }

                for (int i = 1; i < offsetPoints.Count; i++)
                {
                    var p = offsetPoints[i];
                    gcode.Add($"G1 X{p.X:0.000} Y{p.Y:0.000} F{(int)feed}");
                    path.Add($"G1 X{p.X:0.000} Y{p.Y:0.000} F{(int)feed}");
                }

                if (vector.Closed && offsetPoints.Count > 2)
                {
                    var first = offsetPoints[0];
                    gcode.Add($"G1 X{first.X:0.000} Y{first.Y:0.000} F{(int)feed}");
                    path.Add($"G1 X{first.X:0.000} Y{first.Y:0.000} F{(int)feed}");
                }

                if (offsetPoints.Count > 0)
                {
                    var end = offsetPoints[^1];
                    double leadOutX = end.X + params_.LeadOutDistanceMm;
                    gcode.Add($"G1 X{leadOutX:0.000} Y{end.Y:0.000} F{(int)feed}");
                    path.Add($"G1 X{leadOutX:0.000} Y{end.Y:0.000} F{(int)feed}");
                }

                gcode.Add("G0 Z5.0");
            }

            totalLength += PathLength(vector);
        }

        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");

        double cuttingTime = totalLength * maxPassCount / feed * 60.0;

        return new ProfileToolpathResult
        {
            Params = params_,
            GcodeLines = gcode,
            EstimatedTimeSeconds = cuttingTime,
            PassCount = maxPassCount,
            Path = path
        };
    }

    private static double PathLength(VectorShape shape)
    {
        double len = 0;
        for (int i = 1; i < shape.Points.Count; i++)
        {
            len += shape.Points[i - 1].DistanceTo(shape.Points[i]);
        }
        if (shape.Closed && shape.Points.Count > 2)
        {
            len += shape.Points[^1].DistanceTo(shape.Points[0]);
        }
        return len;
    }
}
