namespace VectorPilot.Engine;

public enum ToolpathStrategy
{
    Profile,
    Pocket,
    VCarve,
    Drill,
    Rough3D,
    Finish3D,
    Fluting,
    Texture,
    QuickEngrave,
    Chamfer,
    BevelCarving,
    PhotoVCarve,
    RotaryWrap,
    Inlay,
    Moulding,
    Weave,
    ThreadMill,
    Rounding,
    WrappedFluting,
    DrillBank
}

/// <summary>A calculated toolpath (strategy + parameters + result g-code).</summary>
public sealed class Toolpath
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Exact StrategyRegistry key ("photo-vcarve", "inlay-plug", ...). The Strategy
    /// enum is coarser than the registry, so recalculation keys off this when set.
    /// </summary>
    public string? StrategyKey { get; set; }

    internal void SetId(Guid id) => GetType().GetProperty(nameof(Id))?.SetValue(this, id);
    public string Name { get; set; } = "Toolpath 1";
    public ToolpathStrategy Strategy { get; set; }
    public Guid ToolId { get; set; }
    /// <summary>Strategy params as JSON (registry round-trip for the form).</summary>
    public string ParamsJson { get; set; } = "{}";
    public double CutDepth { get; set; } = 0.25;
    public double StartDepth { get; set; }
    public double Stepdown { get; set; } = 0.125;
    public double StepoverPercent { get; set; } = 40;
    public double FeedRate { get; set; } = 100;
    public double PlungeRate { get; set; } = 50;
    public double SpindleSpeed { get; set; } = 12000;
    public double SafeZ { get; set; } = 0.2;
    public double ClearanceZ { get; set; } = 0.05;
    public bool IsDirty { get; set; } = true;
    /// <summary>Estimated cut time from the last calculation (seconds).</summary>
    public double EstimatedTimeSeconds { get; set; }
    /// <summary>Previous computed G-code (SPK-0316 ghost diff): snapshotted by
    /// SetResult when the new result differs; nil when never generated.</summary>
    public List<string>? PreviousGCode { get; private set; }
    /// <summary>Feed rate used by the last calculation (SPK-1135 job sheet).</summary>
    public double? ParamFeedRate { get; set; }
    /// <summary>Cut depth used by the last calculation (SPK-1135 job sheet).</summary>
    public double? ParamCutDepth { get; set; }
    public List<string> GCode { get; } = new();
    public List<Guid> SelectedShapeIds { get; } = new();

    /// <summary>Write a computed result, snapshotting the outgoing value as
    /// PreviousGCode only when it differs (no-op regen keeps the old path).</summary>
    public void SetResult(IEnumerable<string> gcode)
    {
        var newGcode = gcode.ToList();
        if (GCode.Count > 0 && !newGcode.SequenceEqual(GCode))
        {
            PreviousGCode = GCode.ToList();
        }
        GCode.Clear();
        GCode.AddRange(newGcode);
        IsDirty = false;
    }

    public void MarkDirty() => IsDirty = true;

    public override string ToString() => $"{Name} [{Strategy}]";
}

/// <summary>Tree of toolpaths with dirty propagation (mirrors ShopPilot ToolpathTree).</summary>
public sealed class ToolpathTree
{
    public List<Toolpath> Toolpaths { get; } = new();

    public Toolpath Add(ToolpathStrategy strategy, string? name = null)
    {
        var tp = new Toolpath { Strategy = strategy, Name = name ?? $"{strategy} {Toolpaths.Count + 1}" };
        Toolpaths.Add(tp);
        return tp;
    }

    public void Remove(Guid id) => Toolpaths.RemoveAll(t => t.Id == id);
    public void MarkAllDirty()
    {
        foreach (var t in Toolpaths) t.MarkDirty();
    }
}
