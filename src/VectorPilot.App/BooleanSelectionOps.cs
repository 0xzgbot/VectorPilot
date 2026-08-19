using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>Card A2: applies <see cref="BooleanOps"/> to a canvas selection.</summary>
public static class BooleanSelectionOps
{
    public enum Op { Union, Subtract, Intersect }

    /// <summary>Boolean ops need at least two closed shapes with real outlines.</summary>
    public static bool CanApply(IReadOnlyList<VectorShape> selection)
        => selection.Count >= 2 && selection.All(s => s.Points.Count >= 3);

    /// <summary>
    /// Fold the selection left-to-right with <paramref name="op"/>, replacing the
    /// operands in <paramref name="layer"/> with the resulting rings. Returns the
    /// new shapes, or an empty list when the op is inapplicable or annihilating.
    /// </summary>
    public static List<VectorShape> Apply(Layer layer, IReadOnlyList<VectorShape> selection, Op op)
    {
        if (!CanApply(selection)) return new List<VectorShape>();

        var rings = new List<List<VectorPoint>> { selection[0].Points.ToList() };
        for (int i = 1; i < selection.Count && rings.Count > 0; i++)
            rings = Clip(rings[0], selection[i].Points, op);

        return Commit(layer, selection, rings);
    }

    private static List<List<VectorPoint>> Clip(
        IReadOnlyList<VectorPoint> a, IReadOnlyList<VectorPoint> b, Op op) => op switch
    {
        Op.Union => BooleanOps.Union(a, b),
        Op.Subtract => BooleanOps.Subtract(a, b),
        _ => BooleanOps.Intersect(a, b)
    };

    /// <summary>Swap operands out, results in.</summary>
    private static List<VectorShape> Commit(
        Layer layer, IReadOnlyList<VectorShape> operands, List<List<VectorPoint>> rings)
    {
        foreach (var s in operands) layer.Shapes.Remove(s);

        var made = new List<VectorShape>();
        foreach (var ring in rings.Where(r => r.Count >= 3))
        {
            var shape = new VectorShape { Type = ShapeType.Polyline, Closed = true };
            shape.Points.AddRange(ring);
            layer.Shapes.Add(shape);
            made.Add(shape);
        }
        return made;
    }
}
