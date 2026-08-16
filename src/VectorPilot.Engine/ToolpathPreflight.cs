using VectorPilot.Geometry;

namespace VectorPilot.Engine;

public enum ToolpathPreflightSeverity { Error, Warning }

public sealed class VCarveGateIssue
{
    public int ShapeIndex { get; init; }
    public string SuggestedFix { get; init; } = "Close open vector";
    public string Description { get; init; } = "V-Carve cannot run on open vectors — close them first";
}

public static class VCarveOpenPathGate
{
    public static List<VCarveGateIssue>? Check(IReadOnlyList<VectorShape> shapes)
    {
        var issues = new List<VCarveGateIssue>();
        for (int i = 0; i < shapes.Count; i++)
        {
            if (!shapes[i].Closed) issues.Add(new VCarveGateIssue { ShapeIndex = i });
        }
        return issues.Count == 0 ? null : issues;
    }
}

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
    public static ToolpathPreflightFix SetFlatDepth(double r) => new() { Kind = FixKind.SetFlatDepth, RecommendedMm = r };
    public static ToolpathPreflightFix AddTabs { get; } = new() { Kind = FixKind.AddTabs };
    public static ToolpathPreflightFix SplitFiles { get; } = new() { Kind = FixKind.SplitFiles };
    public static ToolpathPreflightFix UseMeasuredValue { get; } = new() { Kind = FixKind.UseMeasuredValue };
    public static ToolpathPreflightFix WarnOnly { get; } = new() { Kind = FixKind.WarnOnly };
}

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

public static class ToolpathPreflight
{
    public const double FlatDepthSafetyMarginMm = 0.5;

    public static double MaxVDepth(double vBitAngleDegrees, double gapWidthMm)
    {
        double halfAngle = Math.PI / 180.0 * vBitAngleDegrees / 2.0;
        return gapWidthMm / (2.0 * Math.Tan(halfAngle));
    }

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

    public static ToolpathPreflightIssue? VCarvePunchThrough(
        VCarveParams p, IReadOnlyList<VectorShape> vectors,
        double materialThicknessMm, double startDepthMm = 0,
        Guid? nodeId = null, string nodeName = "V-Carve")
    {
        double avail = materialThicknessMm - startDepthMm;
        if (avail <= 0) return null;
        double gap = MaxVectorGapWidth(vectors);
        if (gap <= 0) return null;
        double need = MaxVDepth(p.VBitAngleDegrees, gap);
        if (need <= avail) return null;
        if (p.FlatBottomMode) return null;
        double rec = Math.Max(0.1, avail - FlatDepthSafetyMarginMm);
        return new ToolpathPreflightIssue
        {
            NodeId = nodeId ?? Guid.NewGuid(), NodeName = nodeName,
            RuleId = "R013", Severity = ToolpathPreflightSeverity.Error,
            Message = $"\"{nodeName}\" can go through your material — V-bit must reach {need:0.0}mm to span the widest gap, but only {avail:0.0}mm is available. Set a flat depth to floor the carve.",
            Fix = ToolpathPreflightFix.SetFlatDepth(rec)
        };
    }

    public static ToolpathPreflightIssue? ThroughCutWithoutHoldDown(
        ProfileToolpathParams p, double materialThicknessMm, bool vacuumHoldDown,
        Guid? nodeId = null, string nodeName = "Profile")
    {
        if (p.MaxDepthOfCutMm < materialThicknessMm) return null;
        if (p.AddTabs) return null;
        if (vacuumHoldDown) return null;
        return new ToolpathPreflightIssue
        {
            NodeId = nodeId ?? Guid.NewGuid(), NodeName = nodeName,
            RuleId = "R014", Severity = ToolpathPreflightSeverity.Warning,
            Message = $"\"{nodeName}\" cuts the part free with nothing holding it — it can fly out of place. Add tabs or use hold-down.",
            Fix = ToolpathPreflightFix.AddTabs
        };
    }

    public static ToolpathPreflightIssue? KeepOutZoneViolation(
        string nodeName, IReadOnlyList<KeepOutZone> zones,
        IReadOnlyList<string> gcodeLines, Guid? nodeId = null)
    {
        if (zones.Count == 0) return null;
        var segments = WireframeRenderer.GenerateSegments(gcodeLines);
        foreach (var seg in segments.Where(s => !s.IsRapid))
        {
            foreach (var zone in zones.Where(z => z.IsActive && z.IntersectsLine(seg.Start, seg.End)))
            {
                return new ToolpathPreflightIssue
                {
                    NodeId = nodeId ?? Guid.NewGuid(), NodeName = nodeName,
                    RuleId = "KEEP-OUT", Severity = ToolpathPreflightSeverity.Warning,
                    Message = $"\"{nodeName}\" enters keep-out zone \"{zone.Name}\" — move the toolpath or disable the zone before cutting.",
                    Fix = ToolpathPreflightFix.WarnOnly
                };
            }
        }
        return null;
    }

    public static ToolpathPreflightIssue? MultiToolSingleFile(
        IReadOnlyList<Toolpath> toolpaths, bool supportsToolChange, string nodeName = "Save Toolpaths")
    {
        if (supportsToolChange) return null;
        var buckets = new HashSet<string>();
        foreach (var tp in toolpaths) buckets.Add(tp.ToolId != Guid.Empty ? tp.ToolId.ToString() : "Unassigned");
        if (buckets.Count < 2) return null;
        return new ToolpathPreflightIssue
        {
            NodeName = nodeName, RuleId = "R019",
            Severity = ToolpathPreflightSeverity.Error,
            Message = $"Saving {toolpaths.Count} toolpaths that use {buckets.Count} different tools to a single file, but the selected post can't change tools. Split to multiple files.",
            Fix = ToolpathPreflightFix.SplitFiles
        };
    }

    public static List<(string ToolKey, List<string> Lines)> ToolpathGroupsByTool(IReadOnlyList<Toolpath> toolpaths)
    {
        var groups = new List<(string, List<string>)>();
        var seen = new Dictionary<string, int>();
        foreach (var tp in toolpaths)
        {
            var key = tp.ToolId != Guid.Empty ? tp.ToolId.ToString() : "Unassigned";
            if (!seen.TryGetValue(key, out var idx))
            {
                idx = groups.Count; seen[key] = idx;
                groups.Add((key, new List<string>()));
            }
            groups[idx].Item2.AddRange(tp.GCode);
        }
        return groups;
    }
}

public static class MachineStartPreflight
{
    public const double ThicknessDriftToleranceMm = 0.25;
    public static ToolpathPreflightIssue? ThicknessDrift(double jobThicknessMm, double? measuredThicknessMm, string nodeName = "Machine Start")
    {
        if (measuredThicknessMm is not { } measured) return null;
        if (Math.Abs(measured - jobThicknessMm) <= ThicknessDriftToleranceMm) return null;
        return new ToolpathPreflightIssue
        {
            NodeName = nodeName, RuleId = "R017",
            Severity = ToolpathPreflightSeverity.Warning,
            Message = $"Measured material thickness {measured:0.00}mm differs from the job setup {jobThicknessMm:0.00}mm — verify the cut depth before starting.",
            Fix = ToolpathPreflightFix.UseMeasuredValue
        };
    }
}
