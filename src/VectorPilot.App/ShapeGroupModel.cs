using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Shape grouping (Mac SPK-UXPOLISH parity). A group is a named set of shape ids
/// on a layer: selecting any member selects them all, and transforms apply to the
/// whole set. Groups are a selection concept — the geometry is untouched, so
/// ungrouping is lossless.
/// </summary>
public sealed class ShapeGroup
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Group";
    public HashSet<Guid> ShapeIds { get; } = new();
}

/// <summary>Group registry for the active document.</summary>
public sealed class ShapeGroupModel
{
    private readonly List<ShapeGroup> _groups = new();

    public IReadOnlyList<ShapeGroup> Groups => _groups;

    /// <summary>
    /// Group the given shapes. Shapes already in another group are moved into the
    /// new one (a shape belongs to at most one group), and groups left with fewer
    /// than two members are dissolved.
    /// </summary>
    public ShapeGroup? Group(IEnumerable<VectorShape> shapes, string? name = null)
    {
        var ids = shapes.Select(s => s.Id).Distinct().ToList();
        if (ids.Count < 2) return null;   // a group of one is meaningless

        foreach (var id in ids)
            foreach (var g in _groups)
                g.ShapeIds.Remove(id);

        var group = new ShapeGroup { Name = name ?? $"Group {_groups.Count + 1}" };
        foreach (var id in ids) group.ShapeIds.Add(id);
        _groups.Add(group);

        Prune();
        return group;
    }

    /// <summary>Dissolve every group containing any of these shapes. Returns the count removed.</summary>
    public int Ungroup(IEnumerable<VectorShape> shapes)
    {
        var ids = shapes.Select(s => s.Id).ToHashSet();
        int removed = _groups.RemoveAll(g => g.ShapeIds.Overlaps(ids));
        return removed;
    }

    /// <summary>The group containing this shape, if any.</summary>
    public ShapeGroup? GroupFor(VectorShape shape)
        => _groups.FirstOrDefault(g => g.ShapeIds.Contains(shape.Id));

    /// <summary>
    /// Expand a selection to whole groups: if any member is selected, every
    /// sibling is too. Shapes not in a group pass through unchanged.
    /// </summary>
    public List<VectorShape> ExpandSelection(IEnumerable<VectorShape> selected, Layer layer)
    {
        var result = new List<VectorShape>();
        var seen = new HashSet<Guid>();

        foreach (var shape in selected)
        {
            if (!seen.Add(shape.Id)) continue;
            result.Add(shape);

            if (GroupFor(shape) is not { } group) continue;
            foreach (var sibling in layer.Shapes)
            {
                if (group.ShapeIds.Contains(sibling.Id) && seen.Add(sibling.Id))
                    result.Add(sibling);
            }
        }
        return result;
    }

    /// <summary>Forget shapes that no longer exist, then drop degenerate groups.</summary>
    public void Sync(Layer layer)
    {
        var live = layer.Shapes.Select(s => s.Id).ToHashSet();
        foreach (var g in _groups) g.ShapeIds.RemoveWhere(id => !live.Contains(id));
        Prune();
    }

    public void Clear() => _groups.Clear();

    private void Prune() => _groups.RemoveAll(g => g.ShapeIds.Count < 2);
}
