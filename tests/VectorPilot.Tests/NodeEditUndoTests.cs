using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0201b parity: the node-edit undo contract.</summary>
public class NodeEditUndoTests
{
    private static VectorShape Square() => VectorShape.Rectangle(0, 0, 10, 10);

    [Fact]
    public void MoveNode_Records_And_Undo_Restores_Prior_Point()
    {
        var shape = Square();
        var undo = new NodeEditUndoEngine();
        Assert.True(undo.MoveNode(shape, 0, new VectorPoint(5, 5)));
        Assert.Equal(new VectorPoint(5, 5), shape.Points[0]);
        Assert.True(undo.CanUndo);
        Assert.True(undo.UndoLastMove(shape));
        Assert.Equal(new VectorPoint(0, 0), shape.Points[0]);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Repeated_Moves_Undo_In_Lifo_Order()
    {
        var shape = Square();
        var undo = new NodeEditUndoEngine();
        undo.MoveNode(shape, 1, new VectorPoint(20, 0));
        undo.MoveNode(shape, 1, new VectorPoint(30, 0));
        Assert.Equal(new VectorPoint(30, 0), shape.Points[1]);
        undo.UndoLastMove(shape);
        Assert.Equal(new VectorPoint(20, 0), shape.Points[1]);
        undo.UndoLastMove(shape);
        Assert.Equal(new VectorPoint(10, 0), shape.Points[1]);
        Assert.False(undo.UndoLastMove(shape)); // empty stack
    }

    [Fact]
    public void Noop_Move_Does_Not_Pollute_The_Stack()
    {
        var shape = Square();
        var undo = new NodeEditUndoEngine();
        Assert.False(undo.MoveNode(shape, 0, new VectorPoint(0, 0))); // same point
        Assert.False(undo.CanUndo);
        Assert.Equal(0, undo.PendingMoves);
    }

    [Fact]
    public void Out_Of_Range_Index_Does_Not_Record()
    {
        var shape = Square();
        var undo = new NodeEditUndoEngine();
        Assert.False(undo.MoveNode(shape, 99, new VectorPoint(1, 1)));
        Assert.Equal(0, undo.PendingMoves);
    }

    [Fact]
    public void Session_Snapshot_Restores_Exact_Original_Shape()
    {
        var shape = Square();
        var undo = new NodeEditUndoEngine();
        var preMove = shape.Points.ToList();

        undo.PushSessionSnapshot(shape);
        undo.MoveNode(shape, 0, new VectorPoint(50, 50));
        undo.MoveNode(shape, 1, new VectorPoint(60, 60));
        undo.MoveNode(shape, 2, new VectorPoint(70, 70));

        Assert.True(undo.RestoreSnapshot(shape));
        Assert.Equal(preMove, shape.Points); // exact original node array
        Assert.Equal(0, undo.PendingSessions);
    }

    [Fact]
    public void Undo_On_Other_Shape_Is_Ignored()
    {
        var a = Square();
        var b = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(5, 5));
        var undo = new NodeEditUndoEngine();
        undo.MoveNode(a, 0, new VectorPoint(1, 1));
        Assert.False(undo.UndoLastMove(b)); // wrong shape — rejected, stack intact
        Assert.True(undo.CanUndo);
        Assert.True(undo.UndoLastMove(a));
    }
}
