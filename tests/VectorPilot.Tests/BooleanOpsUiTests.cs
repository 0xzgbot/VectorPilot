using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;
using Op = VectorPilot.App.BooleanSelectionOps.Op;

namespace VectorPilot.Tests;

/// <summary>Card A2 — boolean ops driven from a canvas selection.</summary>
public class BooleanOpsUiTests
{
    private static Layer LayerWith(params VectorShape[] shapes)
    {
        var l = new Layer { Name = "L1" };
        foreach (var s in shapes) l.AddShape(s);
        return l;
    }

    private static double Area(VectorShape s)
    {
        var p = s.Points;
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var q = p[(i + 1) % p.Count];
            a += p[i].X * q.Y - q.X * p[i].Y;
        }
        return Math.Abs(a) / 2;
    }

    [Fact]
    public void CanApply_Needs_Two_Closed_Shapes()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(5, 5));

        Assert.False(BooleanSelectionOps.CanApply(new[] { rect }));          // one shape
        Assert.False(BooleanSelectionOps.CanApply(new[] { rect, line }));    // a 2-point line
        Assert.True(BooleanSelectionOps.CanApply(new[] { rect, VectorShape.Rectangle(5, 5, 10, 10) }));
    }

    [Fact]
    public void Union_Of_Overlapping_Rects_Exceeds_Either_Alone()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, 5, 10, 10);
        var layer = LayerWith(a, b);

        var made = BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Union);

        Assert.NotEmpty(made);
        Assert.Equal(made.Count, layer.Shapes.Count);   // operands replaced
        Assert.DoesNotContain(a, layer.Shapes);
        Assert.True(Area(made[0]) > 100, $"union area {Area(made[0]):F1} should exceed a single 10×10");
    }

    [Fact]
    public void Intersect_Of_Overlapping_Rects_Is_The_Overlap()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, 5, 10, 10);
        var layer = LayerWith(a, b);

        var made = BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Intersect);

        Assert.NotEmpty(made);
        Assert.True(Area(made[0]) < 100, "overlap must be smaller than either operand");
    }

    [Fact]
    public void Intersect_Of_Disjoint_Rects_Yields_Nothing()
    {
        var a = VectorShape.Rectangle(0, 0, 5, 5);
        var b = VectorShape.Rectangle(90, 90, 5, 5);
        var layer = LayerWith(a, b);

        Assert.Empty(BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Intersect));
        Assert.Empty(layer.Shapes);   // operands consumed, nothing produced
    }

    [Fact]
    public void Subtract_Shrinks_The_First_Operand()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, -3, 10, 8);   // diagonal bite; no collinear edges
        var layer = LayerWith(a, b);

        var made = BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Subtract);

        Assert.NotEmpty(made);
        Assert.True(Area(made[0]) < 100, $"remainder {Area(made[0]):F1} must be less than the original 100");
    }

    [Fact]
    public void Results_Are_Closed_Polylines()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, 5, 10, 10);
        var layer = LayerWith(a, b);

        foreach (var s in BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Union))
        {
            Assert.Equal(ShapeType.Polyline, s.Type);
            Assert.True(s.Closed);
            Assert.True(s.Points.Count >= 3);
        }
    }

    [Fact]
    public void Apply_Is_A_Noop_When_Inapplicable()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var layer = LayerWith(a);

        Assert.Empty(BooleanSelectionOps.Apply(layer, new[] { a }, Op.Union));
        Assert.Single(layer.Shapes);   // untouched
    }

    [Fact]
    public void Undo_Restores_Both_Operands()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, 5, 10, 10);
        var layer = LayerWith(a, b);
        var undo = new UndoStack();

        var before = UndoStack.Snapshot(layer);
        BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Union);
        undo.Push("Union", layer, before);

        undo.Undo();
        Assert.Equal(2, layer.Shapes.Count);
        Assert.Equal(100, Area(layer.Shapes[0]), 3);
        Assert.Equal(100, Area(layer.Shapes[1]), 3);
    }

    [Fact]
    public void Union_Folds_Across_Three_Shapes()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(6, 4, 10, 10);
        var c = VectorShape.Rectangle(12, 8, 10, 10);   // staircase; no collinear edges
        var layer = LayerWith(a, b, c);

        var made = BooleanSelectionOps.Apply(layer, new[] { a, b, c }, Op.Union);

        Assert.NotEmpty(made);
        Assert.DoesNotContain(c, layer.Shapes);   // every operand consumed
        Assert.True(Area(made[0]) > 150, $"three-way union {Area(made[0]):F1} should span the staircase");
    }

    /// <summary>
    /// Documents a PRE-EXISTING engine limitation, not an A2 defect: the
    /// Greiner–Hormann clipper conservatively skips degenerate collinear-edge
    /// touches (its header says so), returning operand A unchanged. Recorded so a
    /// future fix has a failing-behaviour baseline to flip.
    /// </summary>
    [Fact]
    public void Collinear_Edge_Case_Is_Conservatively_Skipped()
    {
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(5, 0, 10, 10);   // shares y=0 and y=10 with A
        var layer = LayerWith(a, b);

        var made = BooleanSelectionOps.Apply(layer, new[] { a, b }, Op.Subtract);

        Assert.Single(made);
        Assert.Equal(100, Area(made[0]), 3);   // A returned intact — the known gap
    }
}
