using System.Windows;
using System.Windows.Input;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class DesignPanel
{
    private void Undo_Click(object sender, RoutedEventArgs e) => DoUndo();
    private void Redo_Click(object sender, RoutedEventArgs e) => DoRedo();
    private void Delete_Click(object sender, RoutedEventArgs e) => DoDelete();
    private void Duplicate_Click(object sender, RoutedEventArgs e) => DoDuplicate();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => DoSelectAll();
    private void FlipH_Click(object sender, RoutedEventArgs e) => DoFlip(horizontal: true);
    private void FlipV_Click(object sender, RoutedEventArgs e) => DoFlip(horizontal: false);

    private void Union_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Union);
    private void Subtract_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Subtract);
    private void Intersect_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Intersect);

    /// <summary>Card A2: boolean-combine the selection, undoably.</summary>
    internal void DoBoolean(BooleanSelectionOps.Op op)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked) return;
        if (!BooleanSelectionOps.CanApply(Selection.Selected))
        {
            SetStatus("Select 2+ closed shapes first");
            return;
        }

        var before = UndoStack.Snapshot(layer);
        var made = BooleanSelectionOps.Apply(layer, Selection.Selected, op);
        Undo.Push($"{op}", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        Selection.Clear();
        foreach (var s in made) Selection.Select(s, additive: true);
        SetStatus(made.Count == 0 ? $"{op}: empty result" : $"{op} → {made.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoUndo()
    {
        var label = Undo.Undo();
        Selection.Clear();
        SetStatus(label is null ? "Nothing to undo" : $"Undid: {label}");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoRedo()
    {
        var label = Undo.Redo();
        Selection.Clear();
        SetStatus(label is null ? "Nothing to redo" : $"Redid: {label}");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoDelete()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        int n = Selection.Count;
        Selection.DeleteSelected(layer);
        Undo.Push($"Delete {n} shape(s)", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Deleted {n} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoDuplicate()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        int n = Selection.Count;
        double off = Math.Max(layer.Shapes.Count > 0 ? 5 : 5, WorldTolerance(8));
        Selection.DuplicateSelected(layer, off, off);
        Undo.Push($"Duplicate {n} shape(s)", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Duplicated {n} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoSelectAll()
    {
        var layer = ActiveLayer;
        if (layer is null) return;
        Selection.SelectAll(layer);
        SetStatus($"{Selection.Count} shape(s) selected");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoFlip(bool horizontal)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var b = Selection.SelectionBounds();
        if (b is null) return;

        var before = UndoStack.Snapshot(layer);
        double cx = (b.Value.MinX + b.Value.MaxX) / 2;
        double cy = (b.Value.MinY + b.Value.MaxY) / 2;

        foreach (var shape in Selection.Selected)
        {
            for (int i = 0; i < shape.Points.Count; i++)
            {
                var p = shape.Points[i];
                shape.Points[i] = horizontal
                    ? new VectorPoint(2 * cx - p.X, p.Y)
                    : new VectorPoint(p.X, 2 * cy - p.Y);
            }
        }

        Undo.Push(horizontal ? "Flip Horizontal" : "Flip Vertical", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus(horizontal ? "Flipped horizontally" : "Flipped vertically");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void NudgeSelection(double dx, double dy)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        Selection.MoveSelected(dx, dy);
        Undo.Push("Nudge", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Nudged {Selection.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    /// <summary>Card A1: delete the grabbed node, undoably.</summary>
    internal void DeleteSelectedNodeWithUndo()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || !NodeEdit.IsActive) return;

        var before = UndoStack.Snapshot(layer);
        if (!NodeEdit.DeleteSelectedNode())
        {
            SetStatus("Cannot delete — shape is at its minimum point count");
            return;
        }
        Undo.Push("Delete node", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus("Node deleted");
        RedrawShapes();
        UpdateEditChrome();
    }

    private void DesignPanel_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;

        switch (e.Key)
        {
            case Key.Z when ctrl: DoUndo(); e.Handled = true; break;
            case Key.Y when ctrl: DoRedo(); e.Handled = true; break;
            case Key.D when ctrl: DoDuplicate(); e.Handled = true; break;
            case Key.A when ctrl: DoSelectAll(); e.Handled = true; break;
            case Key.Delete or Key.Back:
                if (NodeEdit.IsActive && NodeEdit.HasSelectedNode) DeleteSelectedNodeWithUndo();
                else DoDelete();
                e.Handled = true;
                break;
            case Key.Escape:
                if (NodeEdit.IsActive)
                {
                    NodeEdit.Exit();
                    SetStatus("Exited node mode");
                }
                else
                {
                    _polylinePoints.Clear();
                    Selection.Clear();
                    SetStatus("Cleared selection");
                }
                RedrawShapes(); UpdateEditChrome();
                e.Handled = true;
                break;
            case Key.Left: NudgeSelection(-step, 0); e.Handled = true; break;
            case Key.Right: NudgeSelection(step, 0); e.Handled = true; break;
            case Key.Up: NudgeSelection(0, -step); e.Handled = true; break;
            case Key.Down: NudgeSelection(0, step); e.Handled = true; break;
        }
    }
}
