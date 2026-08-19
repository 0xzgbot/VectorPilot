using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Shape grouping (Mac SPK-UXPOLISH parity). Grouping is a selection concept:
/// geometry is never modified, so ungroup is lossless.
/// </summary>
public class ShapeGroupTests
{
    private static (Layer, VectorShape, VectorShape, VectorShape) Three()
    {
        var layer = new Layer { Name = "L" };
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Rectangle(20, 0, 10, 10);
        var c = VectorShape.Circle(new VectorPoint(50, 50), 5);
        layer.AddShape(a); layer.AddShape(b); layer.AddShape(c);
        return (layer, a, b, c);
    }

    [Fact]
    public void Grouping_Two_Shapes_Creates_A_Group()
    {
        var (_, a, b, _) = Three();
        var m = new ShapeGroupModel();

        var g = m.Group(new[] { a, b });

        Assert.NotNull(g);
        Assert.Equal(2, g!.ShapeIds.Count);
        Assert.Single(m.Groups);
    }

    [Fact]
    public void A_Group_Of_One_Is_Rejected()
    {
        var (_, a, _, _) = Three();
        var m = new ShapeGroupModel();

        Assert.Null(m.Group(new[] { a }));
        Assert.Empty(m.Groups);
    }

    [Fact]
    public void Selecting_One_Member_Expands_To_The_Whole_Group()
    {
        var (layer, a, b, c) = Three();
        var m = new ShapeGroupModel();
        m.Group(new[] { a, b });

        var expanded = m.ExpandSelection(new[] { a }, layer);

        Assert.Equal(2, expanded.Count);
        Assert.Contains(b, expanded);
        Assert.DoesNotContain(c, expanded);   // ungrouped shape is unaffected
    }

    [Fact]
    public void Ungrouped_Shapes_Pass_Through_Unchanged()
    {
        var (layer, _, _, c) = Three();
        var m = new ShapeGroupModel();

        var expanded = m.ExpandSelection(new[] { c }, layer);
        Assert.Single(expanded);
        Assert.Same(c, expanded[0]);
    }

    [Fact]
    public void Ungroup_Dissolves_The_Group()
    {
        var (layer, a, b, _) = Three();
        var m = new ShapeGroupModel();
        m.Group(new[] { a, b });

        int removed = m.Ungroup(new[] { a });

        Assert.Equal(1, removed);
        Assert.Empty(m.Groups);
        Assert.Single(m.ExpandSelection(new[] { a }, layer));   // no longer expands
    }

    [Fact]
    public void Ungroup_Is_Lossless_For_Geometry()
    {
        var (_, a, b, _) = Three();
        var beforeA = a.Points.Select(p => (p.X, p.Y)).ToList();
        var m = new ShapeGroupModel();

        m.Group(new[] { a, b });
        m.Ungroup(new[] { a });

        Assert.Equal(beforeA, a.Points.Select(p => (p.X, p.Y)).ToList());
    }

    [Fact]
    public void A_Shape_Belongs_To_At_Most_One_Group()
    {
        var (_, a, b, c) = Three();
        var m = new ShapeGroupModel();

        m.Group(new[] { a, b });
        m.Group(new[] { b, c });   // b moves into the new group

        Assert.Single(m.Groups);   // the first group dropped to one member and was pruned
        Assert.Equal(m.Groups[0], m.GroupFor(b));
        Assert.Null(m.GroupFor(a));
    }

    [Fact]
    public void Sync_Forgets_Deleted_Shapes()
    {
        var (layer, a, b, _) = Three();
        var m = new ShapeGroupModel();
        m.Group(new[] { a, b });

        layer.Shapes.Remove(b);
        m.Sync(layer);

        Assert.Empty(m.Groups);   // fell below two members
    }

    [Fact]
    public void GroupFor_Returns_Null_For_Ungrouped()
    {
        var (_, _, _, c) = Three();
        Assert.Null(new ShapeGroupModel().GroupFor(c));
    }

    [Fact]
    public void Groups_Are_Named_Sequentially()
    {
        var (_, a, b, c) = Three();
        var d = VectorShape.Rectangle(70, 70, 5, 5);
        var m = new ShapeGroupModel();

        var g1 = m.Group(new[] { a, b });
        var g2 = m.Group(new[] { c, d });

        Assert.Equal("Group 1", g1!.Name);
        Assert.Equal("Group 2", g2!.Name);
    }
}
