using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// Card A6: component-tree panel model. Owns the ordered component stack,
/// recomposites on any change (order, visibility, combine mode, sculpt), and
/// exposes brush settings for the sculpt tool.
/// </summary>
public sealed class ComponentTreeViewModel
{
    public List<ReliefComponent> Components { get; } = new();
    public HeightfieldData? Composite { get; private set; }

    // Sculpt brush settings surfaced by the panel.
    public BrushShape BrushShape { get; set; } = BrushShape.Sphere;
    public BrushFalloff BrushFalloff { get; set; } = BrushFalloff.Smooth;
    public double BrushRadiusMm { get; set; } = 5.0;
    public double BrushStrength { get; set; } = 0.5;

    public int SelectedIndex { get; set; } = -1;
    public ReliefComponent? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Components.Count ? Components[SelectedIndex] : null;

    public ReliefComponent Add(HeightfieldData hf, string name, OperationMode mode = OperationMode.CombineAdd)
    {
        var c = new ReliefComponent(hf) { Name = name, CombineMode = mode };
        Components.Add(c);
        SelectedIndex = Components.Count - 1;
        Recomposite();
        return c;
    }

    public bool Remove(ReliefComponent c)
    {
        if (!Components.Remove(c)) return false;
        SelectedIndex = Math.Min(SelectedIndex, Components.Count - 1);
        Recomposite();
        return true;
    }

    public void SetVisible(ReliefComponent c, bool visible)
    {
        c.Visible = visible;
        Recomposite();
    }

    public void SetMode(ReliefComponent c, OperationMode mode)
    {
        c.CombineMode = mode;
        Recomposite();
    }

    /// <summary>Move a component within the stack — order changes the result.</summary>
    public bool MoveTo(int from, int to)
    {
        if (from < 0 || from >= Components.Count || to < 0 || to >= Components.Count || from == to) return false;
        var c = Components[from];
        Components.RemoveAt(from);
        Components.Insert(to, c);
        SelectedIndex = to;
        Recomposite();
        return true;
    }

    /// <summary>Apply a sculpt stroke to the selected component's heightfield.</summary>
    public bool Sculpt(SculptTool tool, double x, double y)
    {
        if (Selected is null) return false;

        var stroke = new SculptStrokeParams
        {
            Tool = tool,
            CenterX = x,
            CenterY = y,
            RadiusMm = BrushRadiusMm,
            Strength = BrushStrength,
            BrushShape = BrushShape,
            BrushFalloff = BrushFalloff
        };

        var result = SculptEngine.ApplyStroke(stroke, Selected.Heightfield);
        if (result.CellsAffected == 0) return false;

        // H-302: one-step undo. Snapshot the pre-stroke field so UndoSculpt can
        // restore it; only kept when cells actually changed.
        _preSculptHeightfield = Selected.Heightfield;
        HasSculptUndo = true;

        Selected.Heightfield = result.Heightfield;
        Recomposite();
        return true;
    }

    // ---- H-302: sculpt undo (documented single step) ----

    private HeightfieldData? _preSculptHeightfield;

    /// <summary>True when a stroke has been applied and not yet undone.</summary>
    public bool HasSculptUndo { get; private set; }

    /// <summary>
    /// Restore the heightfield as it was before the LAST stroke (one-step undo —
    /// a stroke chain is intentionally NOT tracked; each new stroke replaces the
    /// snapshot). Returns false when there is nothing to undo.
    /// </summary>
    public bool UndoLastStroke()
    {
        if (!HasSculptUndo || _preSculptHeightfield is null || Selected is null) return false;

        Selected.Heightfield = _preSculptHeightfield;
        _preSculptHeightfield = null;
        HasSculptUndo = false;
        Recomposite();
        return true;
    }

    public void Recomposite() => Composite = ComponentCompositor.Composite(Components);
}
