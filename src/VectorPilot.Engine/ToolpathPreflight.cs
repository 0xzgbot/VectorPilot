using VectorPilot.Geometry;

namespace VectorPilot.Engine;

// ---------------------------------------------------------------------------
// Toolpath preflight rules (ported from ToolpathPreflight.swift + MachineStartPreflight.swift:
// FM-06/07/10/12 → R013/R014/R017/R019, plus SPK-0308 keep-out).
// ---------------------------------------------------------------------------

public enum ToolpathPreflightSeverity { Error, Warning }

/// <summary>SPK-0604: V-Carve open-vector gate issue (blocking, with a
/// plain-English fix CTA targeting the real shape indices).</summary>
public sealed class VCarveGateIssue
{
    public int ShapeIndex { get; init; }
    public string SuggestedFix { get; init; } = "Close open vector";
    public string Description { get; init; } = "V-Carve cannot run on open vectors — close them first";
}

/// <summary>SPK-0604: V-Carve open-vector gate. Returns null when every vector
/// is closed (carve proceeds); otherwise a blocking report whose issues carry
/// the exact indices of the open shapes. Non-open issues (degenerate, gap,
/// self-intersection) do NOT block.</summary>
public static class VCarveOpenPathGate
{
    public static List<VCarveGateIssue>? Check(IReadOnlyList<VectorShape> shapes)
    {
        var issues = new List<VCarveGateIssue>();
        for (int i = 0; i < shapes.Count; i++)
        {
            if (!shapes[i].Closed)
            {
                issues.Add(new VCarveGateIssue { ShapeIndex = i });
            }
        }
        return issues.Count == 0 ? null : issues;
    }
}

/// <summary>The plain-English fix a preflight issue offers (FM mapping CTAs).</summary>
public sealed class ToolpathPreflightFix
{
    public enum FixKind { SetFlatDepth, AddTabs, SplitFiles, UseMeasuredValue, WarnOnly }

    public FixKind Kind { get; init; }
    public double? RecommendedMm { get; init; }

    public string Title => Kind switch
    {
        FixKind.SetFlatDepth => "Set Flat Depth",
        FixKind.AddTabs => "Add Tabs",
        FixKind.SplitFiles => "Split to Multiple Files",
        FixKind.UseMeasuredValue => "Use Measured Value",
        _ => "Warn Only"
    };

    public static ToolpathPreflightFix SetFlatDepth(double recommendedMm) => new() { Kind = FixKind.SetFlatDepth, RecommendedMm = recommendedMm };
    public static ToolpathPreflightFix AddTabs { get; } = new() { Kind = FixKind.AddTabs };
    public static ToolpathPreflightFix SplitFiles { get; } = new() { Kind = FixKind.SplitFiles };
    public static ToolpathPreflightFix UseMeasuredValue { get; } = new() { Kind = FixKind.UseMeasuredValue };
    public static ToolpathPreflightFix WarnOnly { get; } = new() { Kind = FixKind.WarnOnly };
}

/// <summary>A single toolpath-level preflight issue found on a tree node.</summary>
public sealed class ToolpathPreflightIssue
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid NodeId { get; init; }
    public string NodeName { get; init; } = "";
    public string RuleId { get; init; } = "";
    public ToolpathPreflightSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public ToolpathPreflightFix Fix { get; init; } = ToolpathPreflightFix.WarnOnly;
}

/// <summary>Pure preflight rule functions — no UI, no tree dependency (R019/checkTree
/// need the toolpath tree model; they land with the tree in the UI layer).</summary>
public static class ToolpathPreflight
{
    public const double FlatDepthSafetyMarginMm = 0.5;

    /// <summary>Depth the V-bit must reach to span `gapWidth` with a `vBitAngleDegrees`
    /// tool: tipWidth(depth) = 2·depth·tan(halfAngle) → depth = gap / (2·tan(θ/2)).</summary>
    public static double MaxVDepth(double vBitAngleDegrees, double gapWidthMm)
    {
        double halfAngle = Math.PI / 180.0 * vBitAngleDegrees / 2.0;
        return gapWidthMm / (2.0 * Math.Tan(halfAngle));
    }

    /// <summary>Widest "channel" the carve must bridge: the maximum, over every pair
    /// of vectors, of the nearest-point distance between them.</summary>
    public static double MaxVectorGapWidth(IReadOnlyList<VectorShape> vectors)
    {
        if (vectors.Count < 2) return 0;
        double widest = 0;
        for (int i = 0; i < vectors.Count; i++)
        {
            var a = vectors[i];
            if (a.Points.Count == 0) continue;
            for (int j = i + 1; j < vectors.Count; j++)
            {
                var b = vectors[j];
                if (b.Points.Count == 0) continue;
                double nearest = double.MaxValue;
                foreach (var pa in a.Points)
                {
                    foreach (var pb in b.Points)
                    {
                        double dx = pa.X - pb.X, dy = pa.Y - pb.Y;
                        double d = dx * dx + dy * dy;
                        if (d < nearest) nearest = d;
                        if (nearest == 0) break;
                    }
                    if (nearest == 0) break;
                }
                if (nearest > widest) widest = nearest;
            }
        }
        return Math.Sqrt(widest);
    }

    /// <summary>R013: V-bit punch-through (FM-06). Error with a Set Flat Depth CTA.</summary>
    public static ToolpathPreflightIssue? VCarvePunchThrough(
        VCarveParams params_,
        IReadOnlyList<VectorShape> vectors,
        double materialThicknessMm,
        double startDepthMm = 0,
        Guid? nodeId = null,
        string nodeName = "V-Carve")
    {
        double availableDepth = materialThicknessMm - startDepthMm;
        if (availableDepth <= 0) return null; // R009 territory
        double gap = MaxVectorGapWidth(vectors);
        if (gap <= 0) return null;
        double depthNeeded = MaxVDepth(params_.VBitAngleDegrees, gap);
        if (depthNeeded <= availableDepth) return null;
        if (params_.FlatBottomMode) return null; // floor caps the depth

        double recommended = Math.Max(0.1, availableDepth - FlatDepthSafetyMarginMm);
        return new ToolpathPreflightIssue
        {
            NodeId = nodeId ?? Guid.NewGuid(),
            NodeName = nodeName,
            RuleId = "R013",
            Severity = ToolpathPreflightSeverity.Error,
            Message = $"“{nodeName}” can go through your material — the V-bit must reach {depthNeeded:0.0}mm to span the widest gap, but only {availableDepth:0.0}mm is available. Set a flat depth to floor the carve.",
            Fix = ToolpathPreflightFix.SetFlatDepth(recommended)
        };
    }

    /// <summary>R014: through-cut without hold-down (FM-07). Warning with Add Tabs CTA.</summary>
    public static ToolpathPreflightIssue? ThroughCutWithoutHoldDown(
        ProfileToolpathParams params_,
        double materialThicknessMm,
        bool vacuumHoldDown,
        Guid? nodeId = null,
        string nodeName = "Profile")
    {
        if (params_.MaxDepthOfCutMm < materialThicknessMm) return null;
        if (params_.AddTabs) return null;
        if (vacuumHoldDown) return null;
        return new ToolpathPreflightIssue
        {
            NodeId = nodeId ?? Guid.NewGuid(),
            NodeName = nodeName,
            RuleId = "R014",
            Severity = ToolpathPreflightSeverity.Warning,
            Message = $"“{nodeName}” cuts the part free with nothing holding it — it can fly out of place on the last pass. Add tabs or use hold-down.",
            Fix = ToolpathPreflightFix.AddTabs
        };
    }

    /// <summary>SPK-0308: a CUT (non-rapid) segment entering an active keep-out zone.</summary>
    public static ToolpathPreflightIssue? KeepOutZoneViolation(
        string nodeName,
        IReadOnlyList<KeepOutZone> zones,
        IReadOnlyList<string> gcodeLines,
        Guid? nodeId = null)
    {
        if (zones.Count == 0) return null;
        var segments = WireframeRenderer.GenerateSegments(gcodeLines);
        foreach (var segment in segments.Where(s => !s.IsRapid))
        {
            foreach (var zone in zones.Where(z => z.IsActive && z.IntersectsLine(segment.Start, segment.End)))
            {
                return new ToolpathPreflightIssue
                {
                    NodeId = nodeId ?? Guid.NewGuid(),
                    NodeName = nodeName,
                    RuleId = "KEEP-OUT",
                    Severity = ToolpathPreflightSeverity.Warning,
                    Message = $"“{nodeName}” enters keep-out zone “{zone.Name}” — move the toolpath or disable the zone before cutting.",
                    Fix = ToolpathPreflightFix.WarnOnly
                };
            }
        }
        return null;
    }
}

/// <summary>Machine-start preflight (ported from MachineStartPreflight.swift, FM-10 → R017).</summary>
public static class MachineStartPreflight
{
    public const double ThicknessDriftToleranceMm = 0.25;

    /// <summary>R017 — thickness drift warning when measured vs job setup differs beyond tolerance.</summary>
    public static ToolpathPreflightIssue? ThicknessDrift(double jobThicknessMm, double? measuredThicknessMm, string nodeName = "Machine Start")
    {
        if (measuredThicknessMm is not { } measured) return null;
        double drift = Math.Abs(measured - jobThicknessMm);
        if (drift <= ThicknessDriftToleranceMm) return null;
        return new ToolpathPreflightIssue
        {
            NodeName = nodeName,
            RuleId = "R017",
            Severity = ToolpathPreflightSeverity.Warning,
            Message = $"Measured material thickness {measured:0.00}mm differs from the job setup {jobThicknessMm:0.00}mm — verify the cut depth before starting. Use the measured value to update the job.",
            Fix = ToolpathPreflightFix.UseMeasuredValue
        };
    }
}
