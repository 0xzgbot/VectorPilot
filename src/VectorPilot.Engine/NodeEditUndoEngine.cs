using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Node-edit undo (ported from the SPK-0201b node-edit undo contract):
/// moveNode records a prior-point snapshot; undoLastMove restores it LIFO;
/// no-op moves never pollute the stack; a session snapshot-undo restores the
/// exact pre-move node array.
/// </summary>
public sealed class NodeEditUndoEngine
{
    private sealed record MoveEntry(Guid ShapeId, int Index, VectorPoint Prior);
    private sealed record SessionEntry(Guid ShapeId, List<VectorPoint> Snapshot);

    private readonly Stack<MoveEntry> _moves = new();
    private readonly Stack<SessionEntry> _sessions = new();

    public bool CanUndo => _moves.Count > 0;
    public int PendingMoves => _moves.Count;
    public int PendingSessions => _sessions.Count;

    /// <summary>Move a vertex, recording the prior point. Returns false for an
    /// out-of-range index or a no-op (same point) — no-ops never record.</summary>
    public bool MoveNode(VectorShape shape, int index, VectorPoint to)
    {
        if (index < 0 || index >= shape.Points.Count) return false;
        var prior = shape.Points[index];
        if (prior == to) return false;
        shape.Points[index] = to;
        _moves.Push(new MoveEntry(shape.Id, index, prior));
        return true;
    }

    /// <summary>Undo the most recent move on this shape (LIFO). A move recorded
    /// for a DIFFERENT shape is left on the stack (peek-then-pop).</summary>
    public bool UndoLastMove(VectorShape shape)
    {
        if (_moves.Count == 0) return false;
        var entry = _moves.Peek();
        if (entry.ShapeId != shape.Id) return false;
        _moves.Pop();
        if (entry.Index >= shape.Points.Count) return false;
        shape.Points[entry.Index] = entry.Prior;
        return true;
    }

    /// <summary>Snapshot the shape's node array for session-style undo.</summary>
    public void PushSessionSnapshot(VectorShape shape)
        => _sessions.Push(new SessionEntry(shape.Id, shape.Points.ToList()));

    /// <summary>Restore the most recent session snapshot (exact original shape).
    /// A snapshot taken for a DIFFERENT shape is left on the stack.</summary>
    public bool RestoreSnapshot(VectorShape shape)
    {
        if (_sessions.Count == 0) return false;
        var entry = _sessions.Peek();
        if (entry.ShapeId != shape.Id) return false;
        _sessions.Pop();
        shape.Points.Clear();
        shape.Points.AddRange(entry.Snapshot);
        return true;
    }

    public void Clear()
    {
        _moves.Clear();
        _sessions.Clear();
    }
}
