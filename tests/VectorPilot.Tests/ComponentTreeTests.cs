using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ComponentTreeTests
{
    [Fact]
    public void Add_And_Get_Component()
    {
        var tree = new ComponentTree();
        var id = tree.AddComponent("Base");
        Assert.NotNull(tree.GetComponent(id));
        Assert.Equal("Base", tree.GetComponent(id)!.Name);
        Assert.Single(tree.RootComponents);
    }

    [Fact]
    public void Hierarchy_Parent_Child_And_Removal()
    {
        var tree = new ComponentTree();
        var root = tree.AddComponent("Root");
        var child = tree.AddComponent("Child", root);
        var grandchild = tree.AddComponent("Grand", child);

        Assert.Equal(root, tree.GetComponent(child)!.Parent);
        Assert.Contains(child, tree.GetComponent(root)!.Children);
        Assert.Contains(grandchild, tree.GetComponent(child)!.Children);

        // Removing the root removes all descendants.
        tree.RemoveComponent(root);
        Assert.Null(tree.GetComponent(root));
        Assert.Null(tree.GetComponent(child));
        Assert.Null(tree.GetComponent(grandchild));
        Assert.Empty(tree.RootComponents);
    }

    [Fact]
    public void Levels_And_Component_Assignment()
    {
        var tree = new ComponentTree();
        var comp = tree.AddComponent("Part");
        var level = tree.AddLevel("Level 1");
        tree.AddComponentToLevel(comp, level);
        Assert.Contains(comp, tree.GetLevel(level)!.Components);

        tree.RemoveComponent(comp);
        Assert.Empty(tree.GetLevel(level)!.Components);
    }

    [Fact]
    public void Move_Up_Down_Reorders()
    {
        var tree = new ComponentTree();
        var a = tree.AddComponent("A");
        var b = tree.AddComponent("B");
        var c = tree.AddComponent("C");
        tree.MoveComponentDown(a);
        Assert.Equal(new[] { b, a, c }, tree.RootComponents);
        tree.MoveComponentUp(c);
        Assert.Equal(new[] { b, c, a }, tree.RootComponents);
    }
}

public class ComponentModifierEngineTests
{
    private static HeightfieldData Ramp()
    {
        var h = new double[25];
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
                h[j * 5 + i] = j + 1; // rows 1..5
        return new HeightfieldData(5, 5, 1.0, 0, 0, h);
    }

    [Fact]
    public void HeightScale_Multiplies_And_Clamps_NonNegative()
    {
        var r = ComponentModifierEngine.HeightScaled(Ramp(), 2.0);
        Assert.Equal(10.0, r.Heights[24], 6);
        var neg = ComponentModifierEngine.HeightScaled(Ramp(), -3.0);
        Assert.All(neg.Heights, h => Assert.True(h >= 0));
    }

    [Fact]
    public void Tilt_By_360_Is_Noop_And_Tilt_Preserves_Grid()
    {
        var hf = Ramp();
        var same = ComponentModifierEngine.Tilted(hf, 360);
        Assert.Equal(hf.Heights, same.Heights);

        var tilted = ComponentModifierEngine.Tilted(hf, 45);
        Assert.Equal(hf.Width, tilted.Width);
        Assert.Equal(hf.Height, tilted.Height);
        Assert.Equal(hf.CellSizeMm, tilted.CellSizeMm);
        Assert.True(tilted.Heights.Any(v => v > 0));
    }

    [Fact]
    public void Fade_LeftToRight_Reduces_Along_X()
    {
        var f = ComponentModifierEngine.Faded(Ramp(), amount: 0.5, FadeDirection.LeftToRight);
        // Row 0 (all 1.0): left edge 1.0, right edge 0.5.
        Assert.Equal(1.0, f.Heights[0], 6);
        Assert.Equal(0.5, f.Heights[4], 6);
    }

    [Fact]
    public void Fade_Zero_Is_Noop()
    {
        var hf = Ramp();
        var f = ComponentModifierEngine.Faded(hf, 0, FadeDirection.Radial);
        Assert.Equal(hf.Heights, f.Heights);
    }

    [Fact]
    public void Apply_Order_Scale_Tilt_Fade()
    {
        var hf = Ramp();
        var r = ComponentModifierEngine.Apply(hf, heightScale: 2.0, tiltAngleDegrees: null, fadeAmount: 0.25, fadeDirection: FadeDirection.TopToBottom);
        Assert.Equal(hf.Width, r.Width);
        Assert.True(r.MaxHeight <= 10.0);
    }
}
