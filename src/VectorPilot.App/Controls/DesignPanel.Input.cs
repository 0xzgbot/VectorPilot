using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class DesignPanel
{
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var world = ScreenToWorld(e.GetPosition(DrawCanvas));
        var layer = ActiveLayer;
        if (layer is null) return;

        if (CurrentTool == Tool.Node)
        {
            // Double-click a segment inserts a node; single click grabs one.
            if (e.ClickCount >= 2 && NodeEdit.IsActive)
            {
                var beforeIns = UndoStack.Snapshot(layer);
                if (NodeEdit.InsertNodeAt(world))
                {
                    Undo.Push("Insert node", layer, beforeIns);
                    if (AppState.CurrentJob is { } ji) ji.IsDirty = true;
                    SetStatus("Node inserted");
                }
                RedrawShapes();
                UpdateEditChrome();
                return;
            }

            if (!NodeEdit.IsActive)
            {
                var target = SelectionModel.HitTest(layer, world, WorldTolerance(6));
                if (target is null) { SetStatus("Click a shape to edit its nodes"); return; }
                NodeEdit.Enter(target);
                Selection.Select(target);
                SetStatus($"Node mode: {target.Points.Count} point(s) — drag handles, double-click a segment to insert, Del to remove, Esc to exit");
                RedrawShapes();
                return;
            }

            if (NodeEdit.GrabNode(world, WorldTolerance(7)))
            {
                _nodeDragBefore = UndoStack.Snapshot(layer);
                _draggingNode = true;
                DrawCanvas.CaptureMouse();
                SetStatus($"Dragging node {NodeEdit.SelectedNode}");
                RedrawShapes();
            }
            else
            {
                // Clicked empty space in node mode — retarget or exit.
                var next = SelectionModel.HitTest(layer, world, WorldTolerance(6));
                if (next is not null) { NodeEdit.Enter(next); Selection.Select(next); SetStatus("Node mode: new shape"); }
                else { NodeEdit.Exit(); SetStatus("Exited node mode"); }
                RedrawShapes();
            }
            return;
        }

        if (CurrentTool == Tool.Polyline)
        {
            _polylinePoints.Add(world);
            SetStatus($"Polyline: {_polylinePoints.Count} point(s) — right-click to finish");
            RedrawShapes();
            return;
        }

        if (CurrentTool == Tool.Select)
        {
            bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var hit = SelectionModel.HitTest(layer, world, WorldTolerance(6));
            if (hit is not null)
            {
                if (additive) Selection.Toggle(hit);
                else if (!Selection.IsSelected(hit)) Selection.Select(hit);

                if (!layer.Locked && !Selection.IsEmpty)
                {
                    _movingSelection = true;
                    _moveLast = world;
                    _pendingBefore = UndoStack.Snapshot(layer);
                }
            }
            else
            {
                if (!additive) Selection.Clear();
                _dragStart = world; // marquee
            }
            DrawCanvas.CaptureMouse();
            RedrawShapes();
            UpdateEditChrome();
            return;
        }

        _dragStart = world;
        DrawCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var world = ScreenToWorld(e.GetPosition(DrawCanvas));

        if (_draggingNode)
        {
            NodeEdit.DragTo(world);
            SetStatus($"Node {NodeEdit.SelectedNode} → X {world.X:F2}  Y {world.Y:F2}");
            RedrawShapes();
            return;
        }

        if (_movingSelection)
        {
            Selection.MoveSelected(world.X - _moveLast.X, world.Y - _moveLast.Y);
            _moveLast = world;
            SetStatus($"Moving {Selection.Count} shape(s)");
            RedrawShapes();
            return;
        }

        if (_dragStart is null)
        {
            SetStatus($"X {world.X:F2}  Y {world.Y:F2}");
            return;
        }

        if (_preview is not null) { DrawCanvas.Children.Remove(_preview); _preview = null; }
        double dash = Math.Max(WorldTolerance(0.8), 0.03);
        var pen = Brushes.Black;

        switch (CurrentTool)
        {
            case Tool.Select:
            {
                double x = Math.Min(_dragStart.Value.X, world.X), y = Math.Min(_dragStart.Value.Y, world.Y);
                _preview = new Rectangle
                {
                    Width = Math.Abs(world.X - _dragStart.Value.X), Height = Math.Abs(world.Y - _dragStart.Value.Y),
                    Stroke = Brushes.DodgerBlue, StrokeThickness = dash,
                    StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(x, y)
                };
                break;
            }
            case Tool.Rectangle:
            {
                double x = Math.Min(_dragStart.Value.X, world.X), y = Math.Min(_dragStart.Value.Y, world.Y);
                _preview = new Rectangle
                {
                    Width = Math.Abs(world.X - _dragStart.Value.X), Height = Math.Abs(world.Y - _dragStart.Value.Y),
                    Stroke = pen, StrokeThickness = dash,
                    StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(x, y)
                };
                break;
            }
            case Tool.Line:
                _preview = new Line
                {
                    X1 = _dragStart.Value.X, Y1 = _dragStart.Value.Y, X2 = world.X, Y2 = world.Y,
                    Stroke = pen, StrokeThickness = dash,
                    StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false
                };
                break;
            case Tool.Circle:
            {
                double r = _dragStart.Value.DistanceTo(world);
                _preview = new Ellipse
                {
                    Width = r * 2, Height = r * 2, Stroke = pen, StrokeThickness = dash,
                    StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(_dragStart.Value.X - r, _dragStart.Value.Y - r)
                };
                break;
            }
        }
        if (_preview is not null) DrawCanvas.Children.Add(_preview);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        DrawCanvas.ReleaseMouseCapture();
        var world = ScreenToWorld(e.GetPosition(DrawCanvas));
        var layer = ActiveLayer;

        if (_draggingNode)
        {
            _draggingNode = false;
            if (layer is not null && _nodeDragBefore is not null)
            {
                Undo.Push("Move node", layer, _nodeDragBefore);
                _nodeDragBefore = null;
                if (AppState.CurrentJob is { } jn) jn.IsDirty = true;
            }
            SetStatus("Node moved");
            UpdateEditChrome();
            return;
        }

        if (_movingSelection)
        {
            _movingSelection = false;
            if (layer is not null && _pendingBefore is not null)
            {
                Undo.Push($"Move {Selection.Count} shape(s)", layer, _pendingBefore);
                _pendingBefore = null;
                if (AppState.CurrentJob is { } j) j.IsDirty = true;
            }
            SetStatus("Move complete");
            UpdateEditChrome();
            return;
        }

        if (_dragStart is null) return;
        if (_preview is not null) { DrawCanvas.Children.Remove(_preview); _preview = null; }

        if (CurrentTool == Tool.Select)
        {
            if (layer is not null && _dragStart.Value.DistanceTo(world) > WorldTolerance(3))
            {
                bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                Selection.SelectInRect(layer, _dragStart.Value, world, additive);
                SetStatus($"{Selection.Count} shape(s) selected");
            }
            _dragStart = null;
            RedrawShapes();
            UpdateEditChrome();
            return;
        }

        if (layer is null || layer.Locked) { _dragStart = null; return; }

        VectorShape? shape = null;
        switch (CurrentTool)
        {
            case Tool.Rectangle:
            {
                double x = Math.Min(_dragStart.Value.X, world.X), y = Math.Min(_dragStart.Value.Y, world.Y);
                double w = Math.Abs(world.X - _dragStart.Value.X), h = Math.Abs(world.Y - _dragStart.Value.Y);
                if (w > 0.01 && h > 0.01) shape = VectorShape.Rectangle(x, y, w, h);
                break;
            }
            case Tool.Line:
                if (_dragStart.Value.DistanceTo(world) > 0.01) shape = VectorShape.Line(_dragStart.Value, world);
                break;
            case Tool.Circle:
            {
                double r = _dragStart.Value.DistanceTo(world);
                if (r > 0.01) shape = VectorShape.Circle(_dragStart.Value, r);
                break;
            }
        }

        _dragStart = null;
        if (shape is not null)
        {
            var before = UndoStack.Snapshot(layer);
            layer.AddShape(shape);
            Undo.Push($"Draw {CurrentTool}", layer, before);
            if (AppState.CurrentJob is { } job) job.IsDirty = true;
            Selection.Select(shape);
            SetStatus($"Added {CurrentTool}");
            RedrawShapes();
            UpdateEditChrome();
        }
    }

    private void Canvas_RightDown(object sender, MouseButtonEventArgs e)
    {
        // Finish an in-progress polyline.
        var layer = ActiveLayer;
        if (CurrentTool != Tool.Polyline || _polylinePoints.Count < 2 || layer is null || layer.Locked)
        {
            _polylinePoints.Clear();
            return;
        }

        bool close = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var shape = new VectorShape { Type = ShapeType.Polyline, Closed = close };
        shape.Points.AddRange(_polylinePoints);

        var before = UndoStack.Snapshot(layer);
        layer.AddShape(shape);
        Undo.Push(close ? "Draw Polygon" : "Draw Polyline", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        Selection.Select(shape);
        SetStatus($"Polyline finished ({_polylinePoints.Count} points{(close ? ", closed" : "")})");
        _polylinePoints.Clear();
        RedrawShapes();
        UpdateEditChrome();
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var before = ScreenToWorld(e.GetPosition(DrawCanvas));
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double newScale = Math.Clamp(ViewScale.ScaleX * factor, 0.02, 200);
        ViewScale.ScaleX = ViewScale.ScaleY = newScale;

        // keep the cursor anchored on the same world point
        var pos = e.GetPosition(DrawCanvas);
        ViewOffset.X = pos.X - before.X * newScale;
        ViewOffset.Y = pos.Y - before.Y * newScale;

        SetStatus($"Zoom {newScale:F2}×");
        RedrawShapes();
    }
}
