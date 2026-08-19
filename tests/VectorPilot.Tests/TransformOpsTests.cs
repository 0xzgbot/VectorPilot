using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>Card A3 — exact numeric transforms on a selection.</summary>
public class TransformOpsTests
{
    private static VectorShape Rect(double x, double y, double w, double h) => VectorShape.Rectangle(x, y, w, h);

    private static (double W, double H) Size(IReadOnlyList<VectorShape> s)
    {
        var b = TransformOps.Bounds(s)!.Value;
        return (b.MaxX - b.MinX, b.MaxY - b.MinY);
    }

    [Fact]
    public void SetPosition_Moves_Lower_Left_Corner()
    {
        var shapes = new[] { Rect(0, 0, 10, 10) };
        Assert.True(TransformOps.SetPosition(shapes, 25, -7));

        var b = TransformOps.Bounds(shapes)!.Value;
        Assert.Equal(25, b.MinX, 6);
        Assert.Equal(-7, b.MinY, 6);
        Assert.Equal(10, b.MaxX - b.MinX, 6);   // size unchanged
    }

    [Fact]
    public void SetSize_Sets_Exact_Dimensions()
    {
        var shapes = new[] { Rect(0, 0, 10, 10) };
        Assert.True(TransformOps.SetSize(shapes, 40, 20, uniform: false));

        var (w, h) = Size(shapes);
        Assert.Equal(40, w, 6);
        Assert.Equal(20, h, 6);
    }

    [Fact]
    public void SetSize_Uniform_Preserves_Aspect_Ratio()
    {
        var shapes = new[] { Rect(0, 0, 10, 5) };   // 2:1
        Assert.True(TransformOps.SetSize(shapes, 40, 40, uniform: true));

        var (w, h) = Size(shapes);
        Assert.Equal(2.0, w / h, 4);               // still 2:1
        Assert.True(w <= 40.0001 && h <= 40.0001); // fits inside the box
    }

    [Fact]
    public void SetSize_Rejects_Nonpositive()
    {
        var shapes = new[] { Rect(0, 0, 10, 10) };
        Assert.False(TransformOps.SetSize(shapes, 0, 10));
        Assert.False(TransformOps.SetSize(shapes, 10, -5));
        Assert.Equal(10, Size(shapes).W, 6);       // untouched
    }

    [Fact]
    public void RotateBy_90_Twice_Equals_180()
    {
        var once = new[] { Rect(0, 0, 20, 10) };
        var twice = new[] { Rect(0, 0, 20, 10) };

        TransformOps.RotateBy(once, 180);
        TransformOps.RotateBy(twice, 90);
        TransformOps.RotateBy(twice, 90);

        var a = TransformOps.Bounds(once)!.Value;
        var b = TransformOps.Bounds(twice)!.Value;
        Assert.Equal(a.MinX, b.MinX, 6);
        Assert.Equal(a.MinY, b.MinY, 6);
        Assert.Equal(a.MaxX, b.MaxX, 6);
    }

    [Fact]
    public void RotateBy_90_Swaps_Width_And_Height()
    {
        var shapes = new[] { Rect(0, 0, 20, 10) };
        TransformOps.RotateBy(shapes, 90);

        var (w, h) = Size(shapes);
        Assert.Equal(10, w, 5);
        Assert.Equal(20, h, 5);
    }

    [Fact]
    public void RotateBy_Preserves_The_Center()
    {
        var shapes = new[] { Rect(10, 10, 20, 10) };
        var before = TransformOps.Center(shapes);
        TransformOps.RotateBy(shapes, 37);
        var after = TransformOps.Center(shapes);

        Assert.Equal(before.X, after.X, 5);
        Assert.Equal(before.Y, after.Y, 5);
    }

    [Fact]
    public void ScaleBy_Scales_About_The_Center()
    {
        var shapes = new[] { Rect(0, 0, 10, 10) };
        var center = TransformOps.Center(shapes);

        Assert.True(TransformOps.ScaleBy(shapes, 2.0));

        var (w, h) = Size(shapes);
        Assert.Equal(20, w, 6);
        Assert.Equal(20, h, 6);
        var after = TransformOps.Center(shapes);
        Assert.Equal(center.X, after.X, 6);        // center pinned
        Assert.Equal(center.Y, after.Y, 6);
    }

    [Fact]
    public void ScaleBy_Rejects_Nonpositive()
    {
        var shapes = new[] { Rect(0, 0, 10, 10) };
        Assert.False(TransformOps.ScaleBy(shapes, 0));
        Assert.False(TransformOps.ScaleBy(shapes, -2));
        Assert.Equal(10, Size(shapes).W, 6);
    }

    [Fact]
    public void ScaleBy_Also_Scales_Circle_Radius()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 5);
        TransformOps.ScaleBy(new[] { circle }, 3.0);
        Assert.Equal(15, circle.Radius, 6);
    }

    [Fact]
    public void Multi_Shape_Transform_Treats_Selection_As_One_Unit()
    {
        var a = Rect(0, 0, 10, 10);
        var b = Rect(20, 0, 10, 10);
        var shapes = new[] { a, b };

        Assert.True(TransformOps.SetSize(shapes, 60, 20, uniform: false));

        var bounds = TransformOps.Bounds(shapes)!.Value;
        Assert.Equal(60, bounds.MaxX - bounds.MinX, 6);   // combined bbox resized
        Assert.True(SelectionModel.ShapeBounds(a).MaxX < SelectionModel.ShapeBounds(b).MinX,
            "relative arrangement preserved");
    }

    [Fact]
    public void Empty_Selection_Is_Rejected_Everywhere()
    {
        var none = Array.Empty<VectorShape>();
        Assert.Null(TransformOps.Bounds(none));
        Assert.False(TransformOps.SetPosition(none, 1, 1));
        Assert.False(TransformOps.SetSize(none, 5, 5));
        Assert.False(TransformOps.ScaleBy(none, 2));
        Assert.False(TransformOps.RotateBy(none, 45));
    }

    [Fact]
    public void Undo_Restores_Pre_Transform_Geometry()
    {
        var layer = new Layer { Name = "L1" };
        var rect = Rect(0, 0, 10, 10);
        layer.AddShape(rect);
        var undo = new UndoStack();

        var before = UndoStack.Snapshot(layer);
        TransformOps.SetSize(new[] { rect }, 50, 50, uniform: false);
        TransformOps.RotateBy(new[] { rect }, 45);
        undo.Push("Transform", layer, before);

        Assert.NotEqual(10, Size(new[] { layer.Shapes[0] }).W, 3);
        undo.Undo();
        Assert.Equal(10, Size(new[] { layer.Shapes[0] }).W, 6);
    }
}
