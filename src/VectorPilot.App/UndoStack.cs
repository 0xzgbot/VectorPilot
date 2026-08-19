using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// App-level undo/redo stack (SPK-0201b wired into the UI). Each entry is a
/// reversible document mutation captured as before/after shape snapshots on a
/// specific layer, so undo restores exact geometry rather than replaying edits.
/// </summary>
public sealed class UndoStack
{
    /// <summary>One reversible document edit.</summary>
    public sealed class Entry
    {
        public required string Label { get; init; }
        public required Layer Layer { get; init; }
        public required List<VectorShape> Before { get; init; }
        public required List<VectorShape> After { get; init; }
    }

    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the next undoable edit (for menu text), or null.</summary>
    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>Snapshot a layer's shapes (deep copy) for use as a before/after state.</summary>
    public static List<VectorShape> Snapshot(Layer layer)
        => layer.Shapes.Select(CloneShape).ToList();

    public static VectorShape CloneShape(VectorShape s)
    {
        var copy = new VectorShape
        {
            Type = s.Type,
            Radius = s.Radius,
            StartAngleDeg = s.StartAngleDeg,
            EndAngleDeg = s.EndAngleDeg,
            Closed = s.Closed,
            Text = s.Text,
            StrokeWidth = s.StrokeWidth
        };
        copy.Points.AddRange(s.Points);
        return copy;
    }

    /// <summary>Record an edit. Call with the layer state captured BEFORE the mutation.</summary>
    public void Push(string label, Layer layer, List<VectorShape> before)
    {
        _undo.Add(new Entry
        {
            Label = label,
            Layer = layer,
            Before = before,
            After = Snapshot(layer)
        });
        _redo.Clear();
    }

    /// <summary>Undo the most recent edit. Returns the label, or null if nothing to undo.</summary>
    public string? Undo()
    {
        if (_undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(entry.Layer, entry.Before);
        _redo.Add(entry);
        return entry.Label;
    }

    /// <summary>Redo the most recently undone edit.</summary>
    public string? Redo()
    {
        if (_redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(entry.Layer, entry.After);
        _undo.Add(entry);
        return entry.Label;
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }

    private static void Apply(Layer layer, List<VectorShape> state)
    {
        layer.Shapes.Clear();
        foreach (var s in state) layer.Shapes.Add(CloneShape(s));
    }
}
