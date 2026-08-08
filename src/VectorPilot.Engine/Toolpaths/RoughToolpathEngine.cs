using System.Globalization;

namespace VectorPilot.Engine;

// ---------------------------------------------------------------------------
// 3D rough + finish toolpath engines (ported from RoughToolpath.swift and
// HeightfieldToolpath.swift, SPK-3D-spine-b).
// ---------------------------------------------------------------------------

public enum RoughToolpathStrategy { Zigzag, ZigzagAlternate, Offset, Spiral, FollowProfile, Adaptive }

/// <summary>3D rough params (estimator engine; ported from RoughToolpathParams.swift).</summary>
public sealed class RoughToolpathParams
{
    public RoughToolpathStrategy Strategy { get; set; } = RoughToolpathStrategy.Zigzag;
    public double StepOverMm { get; set; } = 0.5;
    public double StepDownMm { get; set; } = 0.25;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 500;
    public double ToolDiameterMm { get; set; } = 6.0;
    public double SafetyHeightMm { get; set; } = 5.0;
    public double ClearanceHeightMm { get; set; } = 2.0;
    public double TopOffsetMm { get; set; }
    public double BottomOffsetMm { get; set; }
    public bool UseZigzag { get; set; } = true;
    public double ZigzagAngle { get; set; }
    public bool TabsEnabled { get; set; }
    public double TabWidthMm { get; set; } = 5.0;
    public double TabSpacingMm { get; set; } = 50.0;

    public void Clamp()
    {
        StepOverMm = Math.Max(0.01, StepOverMm);
        StepDownMm = Math.Max(0.01, StepDownMm);
        FeedRateMmPerMin = Math.Max(1, FeedRateMmPerMin);
        PlungeFeedRateMmPerMin = Math.Max(1, PlungeFeedRateMmPerMin);
        ToolDiameterMm = Math.Max(0.1, ToolDiameterMm);
        SafetyHeightMm = Math.Max(0, SafetyHeightMm);
        ClearanceHeightMm = Math.Max(0, ClearanceHeightMm);
        TabWidthMm = Math.Max(1, TabWidthMm);
        TabSpacingMm = Math.Max(10, TabSpacingMm);
    }
}

public sealed class RoughToolpathResult
{
    public Guid ToolpathId { get; init; } = Guid.NewGuid();
    public Guid ComponentId { get; init; }
    public RoughToolpathStrategy Strategy { get; init; }
    public double TotalPathLengthMm { get; init; }
    public double EstimatedTimeMinutes { get; init; }
    public int ToolChanges { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>3D rough estimator (ported from RoughToolpathEngine.swift — the Mac
/// computes length/time estimates; real G-code comes from HeightfieldRoughEngine).</summary>
public static class RoughToolpathEngine
{
    public static (bool IsValid, List<string> Errors) Validate(RoughToolpathParams p)
    {
        var errors = new List<string>();
        if (p.StepOverMm <= 0) errors.Add("Step over must be positive");
        if (p.StepDownMm <= 0) errors.Add("Step down must be positive");
        if (p.FeedRateMmPerMin <= 0) errors.Add("Feed rate must be positive");
        if (p.PlungeFeedRateMmPerMin <= 0) errors.Add("Plunge feed rate must be positive");
        if (p.ToolDiameterMm <= 0) errors.Add("Tool diameter must be positive");
        if (p.SafetyHeightMm < p.ClearanceHeightMm) errors.Add("Safety height must be >= clearance height");
        return (errors.Count == 0, errors);
    }

    public static RoughToolpathResult Generate(
        RoughToolpathParams config,
        double minZ, double maxZ, double width, double height)
    {
        if (config.StepOverMm > config.ToolDiameterMm)
        {
            return new RoughToolpathResult { Strategy = config.Strategy, Success = false, ErrorMessage = $"Step over ({config.StepOverMm:0.##}mm) exceeds tool diameter ({config.ToolDiameterMm:0.##}mm)" };
        }
        if (config.StepDownMm <= 0)
        {
            return new RoughToolpathResult { Strategy = config.Strategy, Success = false, ErrorMessage = "Step down must be positive" };
        }

        double depth = maxZ - minZ;
        if (depth <= 0)
        {
            return new RoughToolpathResult { Strategy = config.Strategy, Success = false, ErrorMessage = "Component has zero depth" };
        }

        double totalDepth = depth - config.BottomOffsetMm + config.TopOffsetMm;
        int passes = Math.Max(1, (int)Math.Ceiling(totalDepth / config.StepDownMm));

        double area = width * height;
        double stepOver = config.StepOverMm;
        double estimatedPaths = area / (stepOver * Math.Max(width, height));
        double avgPathLength = Math.Max(width, height);
        double totalPathLength = estimatedPaths * avgPathLength * passes;

        double cuttingTimeMinutes = totalPathLength / config.FeedRateMmPerMin;
        double plungeTimeMinutes = passes * 0.5;
        double totalTime = cuttingTimeMinutes + plungeTimeMinutes;
        int toolChanges = Math.Max(1, (int)Math.Ceiling(totalTime / 30.0));

        return new RoughToolpathResult
        {
            Strategy = config.Strategy,
            TotalPathLengthMm = totalPathLength,
            EstimatedTimeMinutes = totalTime,
            ToolChanges = toolChanges,
            Success = true
        };
    }
}
