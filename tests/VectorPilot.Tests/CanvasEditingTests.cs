using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>App-level undo stack + canvas selection model.</summary>
public class CanvasEditingTests
{
    private static Layer MakeLayer(params VectorShape[] shapes)
    {
        var layer = new Layer { Name = "L1" };
        foreach (var s in shapes) layer.AddShape(s);
        return layer;
    }

    [Fact]
    public void Undo_Restores_Exact_Geometry()
    {
        var layer = MakeLayer(VectorShape.Rectangle(0, 0, 10, 10));
        var undo = new UndoStack();

        var before = UndoStack.Snapshot(layer);
        layer.AddShape(VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(5, 5)));
        undo.Push("Draw Line", layer, before);

        Assert.Equal(2, layer.Shapes.Count);
        Assert.True(undo.CanUndo);
        Assert.Equal("Draw Line", undo.NextUndoLabel);

        Assert.Equal("Draw Line", undo.Undo());
        Assert.Single(layer.Shapes);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);
    }

    [Fact]
    public void Redo_Reapplies_The_Edit()
    {
        var layer = MakeLayer();
        var undo = new UndoStack();
        var before = UndoStack.Snapshot(layer);
        layer.AddShape(VectorShape.Circle(new VectorPoint(5, 5), 3));
        undo.Push("Draw Circle", layer, before);

        undo.Undo();
        Assert.Empty(layer.Shapes);
        Assert.Equal("Draw Circle", undo.Redo());
        Assert.Single(layer.Shapes);
        Assert.Equal(3, layer.Shapes[0].Radius, 6);
    }

    [Fact]
    public void New_Edit_Clears_Redo()
    {
        var layer = MakeLayer();
        var undo = new UndoStack();

        var b1 = UndoStack.Snapshot(layer);
        layer.AddShape(VectorShape.Rectangle(0, 0, 5, 5));
        undo.Push("A", layer, b1);
        undo.Undo();
        Assert.True(undo.CanRedo);

        var b2 = UndoStack.Snapshot(layer);
        layer.AddShape(VectorShape.Rectangle(0, 0, 9, 9));
        undo.Push("B", layer, b2);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void HitTest_Finds_Nearest_Shape_Within_Tolerance()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var layer = MakeLayer(line);

        Assert.Same(line, SelectionModel.HitTest(layer, new VectorPoint(5, 0.5), 1.0));
        Assert.Null(SelectionModel.HitTest(layer, new VectorPoint(5, 5), 1.0));
    }

    [Fact]
    public void HitTest_Circle_Hits_The_Ring_Not_The_Middle()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 10);
        var layer = MakeLayer(circle);

        Assert.Same(circle, SelectionModel.HitTest(layer, new VectorPoint(10.2, 0), 1.0));
        Assert.Null(SelectionModel.HitTest(layer, new VectorPoint(0, 0), 1.0));
    }

    [Fact]
    public void Marquee_Selects_Only_Fully_Contained_Shapes()
    {
        var inside = VectorShape.Rectangle(1, 1, 2, 2);
        var outside = VectorShape.Rectangle(50, 50, 5, 5);
        var layer = MakeLayer(inside, outside);
        var sel = new SelectionModel();

        sel.SelectInRect(layer, new VectorPoint(0, 0), new VectorPoint(10, 10));
        Assert.Single(sel.Selected);
        Assert.Same(inside, sel.Selected[0]);
    }

    [Fact]
    public void MoveSelected_Translates_All_Points()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        var layer = MakeLayer(rect);
        var sel = new SelectionModel();
        sel.Select(rect);

        sel.MoveSelected(5, -3);
        var b = SelectionModel.ShapeBounds(rect);
        Assert.Equal(5, b.MinX, 6);
        Assert.Equal(-3, b.MinY, 6);
    }

    [Fact]
    public void Duplicate_Offsets_Copies_And_Selects_Them()
    {
        var rect = VectorShape.Rectangle(0, 0, 4, 4);
        var layer = MakeLayer(rect);
        var sel = new SelectionModel();
        sel.Select(rect);

        var copies = sel.DuplicateSelected(layer, 10, 10);
        Assert.Single(copies);
        Assert.Equal(2, layer.Shapes.Count);
        Assert.Same(copies[0], sel.Selected[0]);          // copies become the selection
        Assert.NotSame(rect, copies[0]);                  // deep copy, not aliased
        Assert.Equal(10, SelectionModel.ShapeBounds(copies[0]).MinX, 6);
        Assert.Equal(0, SelectionModel.ShapeBounds(rect).MinX, 6); // original untouched
    }

    [Fact]
    public void DeleteSelected_Removes_From_Layer_And_Clears()
    {
        var a = VectorShape.Rectangle(0, 0, 2, 2);
        var b = VectorShape.Rectangle(9, 9, 2, 2);
        var layer = MakeLayer(a, b);
        var sel = new SelectionModel();
        sel.Select(a);
        sel.Select(b, additive: true);

        Assert.Equal(2, sel.DeleteSelected(layer));
        Assert.Empty(layer.Shapes);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void SelectionBounds_Spans_Every_Selected_Shape()
    {
        var a = VectorShape.Rectangle(0, 0, 2, 2);
        var b = VectorShape.Rectangle(8, 8, 2, 2);
        var layer = MakeLayer(a, b);
        var sel = new SelectionModel();
        sel.SelectAll(layer);

        var bounds = sel.SelectionBounds()!.Value;
        Assert.Equal(0, bounds.MinX, 6);
        Assert.Equal(10, bounds.MaxX, 6);
        Assert.Equal(10, bounds.MaxY, 6);
    }

    [Fact]
    public void Toggle_Adds_Then_Removes()
    {
        var rect = VectorShape.Rectangle(0, 0, 3, 3);
        var sel = new SelectionModel();
        sel.Toggle(rect);
        Assert.Single(sel.Selected);
        sel.Toggle(rect);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void Undo_After_Move_Restores_Original_Position()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        var layer = MakeLayer(rect);
        var undo = new UndoStack();
        var sel = new SelectionModel();
        sel.Select(rect);

        var before = UndoStack.Snapshot(layer);
        sel.MoveSelected(25, 25);
        undo.Push("Move", layer, before);
        Assert.Equal(25, SelectionModel.ShapeBounds(layer.Shapes[0]).MinX, 6);

        undo.Undo();
        Assert.Equal(0, SelectionModel.ShapeBounds(layer.Shapes[0]).MinX, 6);
    }
}
