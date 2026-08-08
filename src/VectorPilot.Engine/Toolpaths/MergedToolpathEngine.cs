using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Merge mode for combining toolpaths (ported from Swift MergeMode).</summary>
public enum MergeMode
{
    Union,
    Intersection,
    Difference,
    ExclusiveOr
}

/// <summary>Ordering strategy for merged toolpaths (ported from Swift MergeOrderStrategy).</summary>
public enum MergeOrderStrategy
{
    SelectionOrder,
    LeftToRight,
    BottomToTop,
    Grid,
    ShortestPath
}

/// <summary>
/// One source toolpath's G-code fed into a merge, tagged with its tool number so the merged
/// output can insert tool-change lines between blocks that use different tools.
/// </summary>
public sealed class MergeSourceGcode
{
    public string Name { get; set; } = string.Empty;
    public int ToolNumber { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public IReadOnlyList<string> GcodeLines { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Result of a merge (mirrors Swift MergedToolpathResult: mergeMode, sourceIDs, mergedToolpathID,
/// totalSegments, totalLengthMm, success, errorMessage) extended with the concatenated G-code
/// lines — the port's actual deliverable.
/// </summary>
public sealed class MergedToolpathResult
{
    public MergeMode MergeMode { get; init; }
    public List<Guid> SourceIds { get; init; } = new();
    public Guid MergedToolpathId { get; init; } = Guid.NewGuid();
    public int TotalSegments { get; init; }
    public double TotalLengthMm { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> GcodeLines { get; init; } = new();
}

/// <summary>
/// Merged toolpath engine (ported from Swift ArrayCopyAndMergeEngine.mergeToolpaths +
/// MergedToolpath semantics). Faithful behaviour: source toolpath G-code blocks are
/// concatenated in order (SelectionOrder default; LeftToRight / BottomToTop reorder by the
/// blocks' bounding boxes), and a tool-change line (T&lt;n&gt; M6) is inserted between blocks
/// whose tool numbers differ. TotalSegments counts motion lines; TotalLengthMm sums the 2D
/// path lengths of every source block. All numbers are formatted with the invariant culture.
/// </summary>
public static class MergedToolpathEngine
{
    public static MergedToolpathResult Compute(
        IReadOnlyList<MergeSourceGcode> sources,
        MergeOrderStrategy orderStrategy = MergeOrderStrategy.SelectionOrder,
        MergeMode mergeMode = MergeMode.Union,
        bool keepOriginals = true)
    {
        var sourceIds = sources.Select(s => s.Id).ToList();
        if (sources.Count < 2)
        {
            return new MergedToolpathResult
            {
                MergeMode = mergeMode,
                SourceIds = sourceIds,
                TotalSegments = 0,
                TotalLengthMm = 0,
                Success = false,
                ErrorMessage = "Need at least 2 toolpaths to merge"
            };
        }

        var ordered = orderStrategy switch
        {
            MergeOrderStrategy.LeftToRight => sources
                .Select(s => (S: s, MinX: MinXOf(s)))
                .OrderBy(p => p.MinX)
                .Select(p => p.S)
                .ToList(),
            MergeOrderStrategy.BottomToTop => sources
                .Select(s => (S: s, MinY: MinYOf(s)))
                .OrderBy(p => p.MinY)
                .Select(p => p.S)
                .ToList(),
            _ => sources.ToList() // SelectionOrder / Grid / ShortestPath: keep given order
        };

        var g = new List<string>();
        int totalSegments = 0;
        double totalLength = 0;
        int? lastTool = null;

        foreach (var source in ordered)
        {
            var lines = source.GcodeLines ?? Array.Empty<string>();
            totalSegments += lines.Count(GcodeMotion.IsMotionLine);
            totalLength += PathLength2D(lines);

            if (!string.IsNullOrEmpty(source.Name))
            {
                g.Add($"({source.Name})");
            }

            if (lastTool.HasValue && source.ToolNumber != lastTool.Value)
            {
                g.Add("(Tool change)");
                g.Add($"T{source.ToolNumber} M6");
            }
            lastTool = source.ToolNumber;

            g.AddRange(lines);
        }

        return new MergedToolpathResult
        {
            MergeMode = mergeMode,
            SourceIds = sourceIds,
            TotalSegments = totalSegments,
            TotalLengthMm = totalLength,
            Success = true,
            GcodeLines = g
        };
    }

    private static double MinXOf(MergeSourceGcode source)
    {
        double min = double.PositiveInfinity;
        foreach (var line in source.GcodeLines ?? Array.Empty<string>())
        {
            if (GcodeMotion.TryGetPoint(line, out var p)) min = Math.Min(min, p.X);
        }
        return double.IsPositiveInfinity(min) ? 0.0 : min;
    }

    private static double MinYOf(MergeSourceGcode source)
    {
        double min = double.PositiveInfinity;
        foreach (var line in source.GcodeLines ?? Array.Empty<string>())
        {
            if (GcodeMotion.TryGetPoint(line, out var p)) min = Math.Min(min, p.Y);
        }
        return double.IsPositiveInfinity(min) ? 0.0 : min;
    }

    private static double PathLength2D(IReadOnlyList<string> lines)
    {
        double len = 0;
        VectorPoint? last = null;
        foreach (var line in lines)
        {
            if (!GcodeMotion.TryGetPoint(line, out var p)) continue;
            if (last.HasValue) len += last.Value.DistanceTo(p);
            last = p;
        }
        return len;
    }
}
