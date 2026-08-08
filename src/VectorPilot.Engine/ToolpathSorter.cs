namespace VectorPilot.Engine;

/// <summary>Toolpath sort strategies (Aspire toolpath-arrangement parity).</summary>
public enum ToolpathSortMode
{
    ByTool,      // group by tool, smallest diameter first
    ByDepth,     // shallowest first (safest for thin parts)
    ByName,      // alphabetical
    ByColor,     // tree color order
    Manual       // tree order as-is
}

/// <summary>
/// Toolpath sorter: orders calculated toolpaths for cutting — group by tool
/// (one tool change per group), shallow-first, or by name.
/// </summary>
public static class ToolpathSorter
{
    public static List<Toolpath> Sort(IEnumerable<Toolpath> toolpaths, ToolpathSortMode mode)
    {
        var list = toolpaths.ToList();
        switch (mode)
        {
            case ToolpathSortMode.ByTool:
                // Group by tool diameter (ascending), stable within groups.
                return list
                    .OrderBy(t => ToolDiameterOf(t))
                    .ThenBy(t => list.IndexOf(t))
                    .ToList();
            case ToolpathSortMode.ByDepth:
                return list.OrderBy(t => t.CutDepth).ThenBy(t => list.IndexOf(t)).ToList();
            case ToolpathSortMode.ByName:
                return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            default:
                return list;
        }
    }

    /// <summary>Count tool changes when cutting in the given order (same tool = no change).</summary>
    public static int ToolChanges(IReadOnlyList<Toolpath> ordered)
    {
        int changes = 0;
        string? prev = null;
        foreach (var t in ordered)
        {
            string tool = t.ToolId != Guid.Empty ? t.ToolId.ToString() : $"d{ToolDiameterOf(t):0.##}";
            if (prev is not null && prev != tool) changes++;
            prev = tool;
        }
        return changes;
    }

    /// <summary>Merge contiguous toolpaths of the same tool into one cut session
    /// (Aspire merged-toolpath parity): returns group boundaries.</summary>
    public static List<List<Toolpath>> MergeByTool(IReadOnlyList<Toolpath> ordered)
    {
        var groups = new List<List<Toolpath>>();
        List<Toolpath>? current = null;
        string? currentTool = null;
        foreach (var t in ordered)
        {
            string tool = t.ToolId != Guid.Empty ? t.ToolId.ToString() : $"d{ToolDiameterOf(t):0.##}";
            if (current is null || currentTool != tool)
            {
                current = new List<Toolpath>();
                groups.Add(current);
                currentTool = tool;
            }
            current.Add(t);
        }
        return groups;
    }

    private static double ToolDiameterOf(Toolpath t)
    {
        // Fall back to a diameter derived from stepover/cut geometry when no tool is set.
        return t.ToolId != Guid.Empty ? 3.175 : 1.0 + t.CutDepth * 0.5;
    }
}
