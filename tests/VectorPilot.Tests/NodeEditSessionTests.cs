using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>Card A1 — canvas node editing.</summary>
public class NodeEditSessionTests
{
    private static VectorShape Square() => VectorShape.Rectangle(0, 0, 10, 10);

    private static VectorShape OpenPath()
    {
        var s = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        s.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(10, 0), new VectorPoint(10, 10) });
        return s;
    }

    [Fact]
    public void Enter_And_Exit_Toggle_Active()
    {
        var s = new NodeEditSession();
        Assert.False(s.IsActive);
        s.Enter(Square());
        Assert.True(s.IsActive);
        s.Exit();
        Assert.False(s.IsActive);
        Assert.False(s.HasSelectedNode);
    }

    [Fact]
    public void GrabNode_Selects_The_Nearest_Handle_Within_Tolerance()
    {
        var shape = Square();
        var s = new NodeEditSession();
        s.Enter(shape);

        Assert.True(s.GrabNode(new VectorPoint(0.3, 0.3), 1.0));
        Assert.True(s.HasSelectedNode);
        Assert.Equal(shape.Points[s.SelectedNode], shape.Points.OrderBy(p => p.X + p.Y).First());
    }

    [Fact]
    public void GrabNode_Misses_Outside_Tolerance()
    {
        var s = new NodeEditSession();
        s.Enter(Square());
        Assert.False(s.GrabNode(new VectorPoint(5, 5), 0.5));
        Assert.False(s.HasSelectedNode);
    }

    [Fact]
    public void DragTo_Moves_Only_The_Grabbed_Node()
    {
        var shape = Square();
        int before = shape.Points.Count;
        var s = new NodeEditSession();
        s.Enter(shape);
        s.GrabNode(shape.Points[0], 0.5);

        Assert.True(s.DragTo(new VectorPoint(-5, -5)));
        Assert.Equal(-5, shape.Points[0].X, 6);
        Assert.Equal(before, shape.Points.Count);
    }

    [Fact]
    public void DragTo_Without_A_Grab_Is_A_Noop()
    {
        var s = new NodeEditSession();
        s.Enter(Square());
        Assert.False(s.DragTo(new VectorPoint(1, 1)));
    }

    [Fact]
    public void InsertNodeAt_Splits_The_Nearest_Segment()
    {
        var shape = OpenPath();
        int before = shape.Points.Count;
        var s = new NodeEditSession();
        s.Enter(shape);

        // Midpoint of the first segment (0,0)-(10,0).
        Assert.True(s.InsertNodeAt(new VectorPoint(5, 0)));
        Assert.Equal(before + 1, shape.Points.Count);
        Assert.Equal(5, shape.Points[1].X, 6);   // landed between the endpoints
        Assert.Equal(1, s.SelectedNode);          // and is selected
    }

    [Fact]
    public void DeleteSelectedNode_Removes_It()
    {
        var shape = Square();
        int before = shape.Points.Count;
        var s = new NodeEditSession();
        s.Enter(shape);
        s.GrabNode(shape.Points[0], 0.5);

        Assert.True(s.DeleteSelectedNode());
        Assert.Equal(before - 1, shape.Points.Count);
        Assert.False(s.HasSelectedNode);
    }

    [Fact]
    public void DeleteSelectedNode_Refuses_Below_Minimum()
    {
        var line = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        line.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(5, 0) });

        var s = new NodeEditSession();
        s.Enter(line);
        s.GrabNode(new VectorPoint(0, 0), 0.5);

        Assert.False(s.DeleteSelectedNode());   // a 2-point open path cannot shrink
        Assert.Equal(2, line.Points.Count);
    }

    [Fact]
    public void Handles_Expose_Points_Only_While_Active()
    {
        var shape = Square();
        var s = new NodeEditSession();
        Assert.Empty(s.Handles);
        s.Enter(shape);
        Assert.Equal(shape.Points.Count, s.Handles.Count);
        s.Exit();
        Assert.Empty(s.Handles);
    }

    [Fact]
    public void Undo_Restores_Geometry_After_A_Node_Drag()
    {
        var layer = new Layer { Name = "L1" };
        var shape = Square();
        layer.AddShape(shape);

        var undo = new UndoStack();
        var session = new NodeEditSession();
        session.Enter(shape);

        var before = UndoStack.Snapshot(layer);
        session.GrabNode(shape.Points[0], 0.5);
        session.DragTo(new VectorPoint(-20, -20));
        undo.Push("Move node", layer, before);

        Assert.Equal(-20, layer.Shapes[0].Points[0].X, 6);
        undo.Undo();
        Assert.Equal(0, layer.Shapes[0].Points[0].X, 6);
    }

    [Fact]
    public void Undo_Restores_Point_Count_After_Insert_And_Delete()
    {
        var layer = new Layer { Name = "L1" };
        var shape = OpenPath();
        layer.AddShape(shape);
        int original = shape.Points.Count;

        var undo = new UndoStack();
        var session = new NodeEditSession();
        session.Enter(shape);

        var before = UndoStack.Snapshot(layer);
        session.InsertNodeAt(new VectorPoint(5, 0));
        undo.Push("Insert node", layer, before);
        Assert.Equal(original + 1, layer.Shapes[0].Points.Count);

        undo.Undo();
        Assert.Equal(original, layer.Shapes[0].Points.Count);
        undo.Redo();
        Assert.Equal(original + 1, layer.Shapes[0].Points.Count);
    }

    [Fact]
    public void Grab_On_Empty_Shape_Is_Safe()
    {
        var empty = new VectorShape { Type = ShapeType.Polyline };
        var s = new NodeEditSession();
        s.Enter(empty);
        Assert.False(s.GrabNode(new VectorPoint(0, 0), 5));
        Assert.False(s.DeleteSelectedNode());
    }
}
