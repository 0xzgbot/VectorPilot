using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// Card A4: view-model behind the tool browser. Groups the catalog by tool class,
/// resolves cut data through the 3-part linkage (machine override → per-material →
/// derived defaults), and supports edit/save/revert against a working copy so
/// Cancel cannot corrupt the database.
/// </summary>
public sealed class ToolBrowserViewModel
{
    private readonly ToolDatabase _db;
    private readonly Dictionary<Guid, ToolCutData> _pending = new();

    public string? Material { get; set; } = "hardwood";
    public string? MachineName { get; set; }

    public ToolBrowserViewModel(ToolDatabase db) => _db = db;

    /// <summary>The backing database (so callers can persist after Save).</summary>
    public ToolDatabase Database => _db;

    public IReadOnlyList<Tool> Tools => _db.Tools;

    /// <summary>Tool classes actually present, in catalog order.</summary>
    public List<ToolType> Classes =>
        _db.Tools.Select(t => t.Type).Distinct().OrderBy(t => t.ToString(), StringComparer.Ordinal).ToList();

    public List<Tool> ToolsOfClass(ToolType type) => _db.Tools.Where(t => t.Type == type).ToList();

    /// <summary>Resolved feed/plunge/rpm/depth for the current material + machine.</summary>
    public ResolvedCutData Resolve(Tool tool) => tool.ResolvedCutData(Material, MachineName);

    /// <summary>True when the tool has unsaved edits in this session.</summary>
    public bool IsDirty(Tool tool) => _pending.ContainsKey(tool.Id);

    public bool HasPendingEdits => _pending.Count > 0;

    /// <summary>
    /// Stage a cut-data edit for the current material. Nothing touches the
    /// database until <see cref="Save"/>.
    /// </summary>
    public void Edit(Tool tool, double feed, double plunge, double rpm, double depth)
    {
        _pending[tool.Id] = new ToolCutData
        {
            Material = Material ?? "hardwood",
            FeedRateMmPerMin = feed,
            PlungeRateMmPerMin = plunge,
            SpindleRpm = rpm,
            MaxDepthOfCutMm = depth
        };
    }

    /// <summary>Commit staged edits into the database. Returns how many tools changed.</summary>
    public int Save()
    {
        int n = 0;
        foreach (var (id, cut) in _pending)
        {
            var tool = _db.ToolWithId(id);
            if (tool is null) continue;

            var existing = tool.CutData.FirstOrDefault(c =>
                c.Material.Equals(cut.Material, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) tool.CutData.Remove(existing);

            tool.CutData.Add(cut);
            tool.UpdatedAt = DateTime.UtcNow;
            n++;
        }
        _pending.Clear();
        return n;
    }

    /// <summary>Throw away staged edits.</summary>
    public void Revert() => _pending.Clear();

    /// <summary>Peek at a staged edit (null when the tool is clean).</summary>
    public ToolCutData? PendingFor(Tool tool)
        => _pending.TryGetValue(tool.Id, out var c) ? c : null;
}
