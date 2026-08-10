namespace VectorPilot.Engine;

/// <summary>
/// Resimulation planner (SPK-0315 selective resim): decides which toolpaths
/// must be recalculated after a dirty-region change, and in what order they
/// should be cut.
/// </summary>
public static class ToolpathResimPlanner
{
    /// <summary>Toolpaths whose selected shapes intersect any dirty region
    /// (FullTree / keep-out regions affect everything).</summary>
    public static List<Toolpath> AffectedToolpaths(IEnumerable<Toolpath> toolpaths, DirtyRegionManager dirty)
        => toolpaths.Where(tp => dirty.Affects(tp.SelectedShapeIds)).ToList();

    /// <summary>Order toolpaths for cutting via the shared sorter.</summary>
    public static List<Toolpath> SortForCut(IEnumerable<Toolpath> toolpaths, ToolpathSortMode mode)
        => ToolpathSorter.Sort(toolpaths, mode);
}
