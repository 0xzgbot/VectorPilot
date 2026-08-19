using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Node-edit session for the canvas (card A1): tracks which shape is being
/// node-edited, which handle is grabbed, and applies insert/move/delete through
/// <see cref="NodeEditEngine"/>. Pure logic — no WPF — so it is unit-testable.
/// </summary>
public sealed class NodeEditSession
{
    public VectorShape? Shape { get; private set; }
    public int SelectedNode { get; private set; } = -1;
    public bool IsActive => Shape is not null;
    public bool HasSelectedNode => SelectedNode >= 0;

    /// <summary>Enter node mode on a shape.</summary>
    public void Enter(VectorShape shape)
    {
        Shape = shape;
        SelectedNode = -1;
    }

    /// <summary>Leave node mode (Esc).</summary>
    public void Exit()
    {
        Shape = null;
        SelectedNode = -1;
    }

    /// <summary>Grab the node nearest <paramref name="p"/> within tolerance. Returns true when one was grabbed.</summary>
    public bool GrabNode(VectorPoint p, double tolerance)
    {
        if (Shape is null || Shape.Points.Count == 0) { SelectedNode = -1; return false; }

        int nearest = -1;
        double best = double.MaxValue;
        for (int i = 0; i < Shape.Points.Count; i++)
        {
            double d = Dist(Shape.Points[i], p);
            if (d <= tolerance && d < best) { best = d; nearest = i; }
        }
        SelectedNode = nearest;
        return nearest >= 0;
    }

    /// <summary>Drag the grabbed node to a new position.</summary>
    public bool DragTo(VectorPoint to)
    {
        if (Shape is null || SelectedNode < 0 || SelectedNode >= Shape.Points.Count) return false;
        Shape.Points[SelectedNode] = to;
        return true;
    }

    /// <summary>Insert a node on the segment nearest <paramref name="p"/> and select it.</summary>
    public bool InsertNodeAt(VectorPoint p)
    {
        if (Shape is null || Shape.Points.Count < 2) return false;
        var pts = Shape.Points.ToList();
        if (!NodeEditEngine.SplitEdge(pts, p, out int inserted)) return false;

        Shape.Points.Clear();
        Shape.Points.AddRange(pts);
        SelectedNode = inserted;
        return true;
    }

    /// <summary>Delete the selected node. Refuses below the minimum viable point count.</summary>
    public bool DeleteSelectedNode()
    {
        if (Shape is null || SelectedNode < 0 || SelectedNode >= Shape.Points.Count) return false;

        int min = Shape.Closed ? 3 : 2;
        if (Shape.Points.Count <= min) return false;

        Shape.Points.RemoveAt(SelectedNode);
        SelectedNode = -1;
        return true;
    }

    /// <summary>Handle positions for rendering (empty when inactive).</summary>
    public IReadOnlyList<VectorPoint> Handles
        => Shape is null ? Array.Empty<VectorPoint>() : Shape.Points;

    private static double Dist(VectorPoint a, VectorPoint b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
