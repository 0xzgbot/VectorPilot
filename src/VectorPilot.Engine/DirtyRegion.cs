namespace VectorPilot.Engine;

/// <summary>Dirty-region types (ported from DirtyRegion.swift, SPK-0315/0316).</summary>
public enum DirtyRegionType
{
    VectorModified,
    BatchChange,
    FullTree,
    KeepOutZoneChanged
}

public sealed class DirtyRegion
{
    public DirtyRegionType Type { get; init; }
    public List<Guid> Ids { get; init; } = new();
    public int AffectedCount => Type switch
    {
        DirtyRegionType.VectorModified => 1,
        DirtyRegionType.BatchChange => Ids.Count,
        DirtyRegionType.FullTree => int.MaxValue,
        _ => -1 // keep-out zone: all toolpaths affected
    };
}

/// <summary>
/// Dirty-region manager (SPK-0315 dirty-region resim): tracks which vectors
/// changed and decides which toolpaths need recalculation — a toolpath is
/// affected when its selected vectors intersect the dirty set.
/// </summary>
public sealed class DirtyRegionManager
{
    public List<DirtyRegion> DirtyRegions { get; } = new();
    public bool NeedsResimulation { get; private set; }

    public void MarkVectorModified(Guid vectorId)
    {
        DirtyRegions.Add(new DirtyRegion { Type = DirtyRegionType.VectorModified, Ids = { vectorId } });
        NeedsResimulation = true;
    }

    public void MarkBatchChange(IReadOnlyList<Guid> ids)
    {
        if (ids.Count > 0)
        {
            DirtyRegions.Add(new DirtyRegion { Type = DirtyRegionType.BatchChange, Ids = ids.ToList() });
            NeedsResimulation = true;
        }
    }

    public void MarkFullTreeDirty()
    {
        DirtyRegions.Add(new DirtyRegion { Type = DirtyRegionType.FullTree });
        NeedsResimulation = true;
    }

    public void MarkKeepOutZoneChanged()
    {
        DirtyRegions.Add(new DirtyRegion { Type = DirtyRegionType.KeepOutZoneChanged });
        NeedsResimulation = true;
    }

    /// <summary>True when a toolpath whose selected vectors are `toolpathShapeIds`
    /// must be recalculated (SPK-0315 selective resim).</summary>
    public bool Affects(IReadOnlyCollection<Guid> toolpathShapeIds)
    {
        foreach (var region in DirtyRegions)
        {
            switch (region.Type)
            {
                case DirtyRegionType.FullTree:
                case DirtyRegionType.KeepOutZoneChanged:
                    return true;
                case DirtyRegionType.VectorModified:
                case DirtyRegionType.BatchChange:
                    if (region.Ids.Any(toolpathShapeIds.Contains)) return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>Clear all dirty regions (after resimulation).</summary>
    public void Clear()
    {
        DirtyRegions.Clear();
        NeedsResimulation = false;
    }
}
