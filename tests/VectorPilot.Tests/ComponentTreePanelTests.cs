using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>Card A6 — component tree: combine modes, order, visibility, sculpt.</summary>
public class ComponentTreePanelTests
{
    private static HeightfieldData Grid(int w, int h, double value)
    {
        var heights = new double[w * h];
        Array.Fill(heights, value);
        return new HeightfieldData(w, h, 1.0, 0, 0, heights);
    }

    private static ComponentTreeViewModel WithTwo(double a = 3, double b = 4)
    {
        var vm = new ComponentTreeViewModel();
        vm.Add(Grid(4, 4, a), "A");
        vm.Add(Grid(4, 4, b), "B");
        return vm;
    }

    [Fact]
    public void Add_Selects_The_New_Component_And_Composites()
    {
        var vm = new ComponentTreeViewModel();
        var c = vm.Add(Grid(4, 4, 2), "First");

        Assert.Single(vm.Components);
        Assert.Same(c, vm.Selected);
        Assert.NotNull(vm.Composite);
    }

    [Fact]
    public void Composite_Is_Null_With_No_Components()
    {
        var vm = new ComponentTreeViewModel();
        vm.Recomposite();
        Assert.Null(vm.Composite);
    }

    [Fact]
    public void Invisible_Components_Are_Excluded()
    {
        var vm = WithTwo(3, 4);
        double bothVisible = vm.Composite!.MaxHeight;

        vm.SetVisible(vm.Components[1], false);
        double onlyFirst = vm.Composite!.MaxHeight;

        Assert.NotEqual(bothVisible, onlyFirst);
        Assert.Equal(3, onlyFirst, 6);
    }

    [Fact]
    public void Changing_Combine_Mode_Recomposites()
    {
        var vm = WithTwo(3, 4);
        vm.SetMode(vm.Components[1], OperationMode.CombineAdd);
        double added = vm.Composite!.MaxHeight;

        vm.SetMode(vm.Components[1], OperationMode.CombineSubtract);
        double subtracted = vm.Composite!.MaxHeight;

        Assert.True(added > subtracted, $"add {added} should exceed subtract {subtracted}");
    }

    [Fact]
    public void Subtract_Clamps_At_Zero()
    {
        var vm = WithTwo(2, 9);
        vm.SetMode(vm.Components[1], OperationMode.CombineSubtract);
        Assert.Equal(0, vm.Composite!.MaxHeight, 6);
    }

    [Fact]
    public void Merge_Takes_The_Higher_Surface()
    {
        var vm = WithTwo(3, 7);
        vm.SetMode(vm.Components[1], OperationMode.CombineMerge);
        Assert.Equal(7, vm.Composite!.MaxHeight, 6);
    }

    [Fact]
    public void Low_Takes_The_Lower_Surface()
    {
        var vm = WithTwo(3, 7);
        vm.SetMode(vm.Components[1], OperationMode.CombineLow);
        Assert.Equal(3, vm.Composite!.MaxHeight, 6);
    }

    [Fact]
    public void Order_Changes_The_Result()
    {
        var vm = WithTwo(2, 8);
        vm.SetMode(vm.Components[1], OperationMode.CombineSubtract);
        double aMinusB = vm.Composite!.MaxHeight;      // base A(2) − B(8) → clamped 0

        Assert.True(vm.MoveTo(1, 0));                  // B becomes the base; A keeps Add
        double reordered = vm.Composite!.MaxHeight;    // base B(8) + A(2), capped at maxH

        Assert.NotEqual(aMinusB, reordered);
        Assert.Equal(0, aMinusB, 6);
        Assert.Equal(8, reordered, 6);
    }

    [Fact]
    public void MoveTo_Rejects_Out_Of_Range()
    {
        var vm = WithTwo();
        Assert.False(vm.MoveTo(0, 5));
        Assert.False(vm.MoveTo(-1, 0));
        Assert.False(vm.MoveTo(0, 0));                 // no-op
    }

    [Fact]
    public void Remove_Recomposites_And_Reselects()
    {
        var vm = WithTwo(3, 4);
        Assert.True(vm.Remove(vm.Components[1]));
        Assert.Single(vm.Components);
        Assert.Equal(3, vm.Composite!.MaxHeight, 6);
        Assert.NotNull(vm.Selected);
    }

    [Fact]
    public void Sculpt_Modifies_The_Selected_Component()
    {
        var vm = new ComponentTreeViewModel();
        vm.Add(Grid(20, 20, 5), "Base");
        double before = vm.Composite!.MaxHeight;

        vm.BrushRadiusMm = 6;
        vm.BrushStrength = 0.8;
        Assert.True(vm.Sculpt(SculptTool.Inflate, 10, 10));

        Assert.True(vm.Composite!.MaxHeight > before,
            $"inflate should raise the surface: {before} -> {vm.Composite!.MaxHeight}");
    }

    [Fact]
    public void Sculpt_Without_A_Selection_Is_Refused()
    {
        var vm = new ComponentTreeViewModel();
        Assert.False(vm.Sculpt(SculptTool.Brush, 0, 0));
    }

    [Fact]
    public void Sculpt_Outside_The_Field_Affects_Nothing()
    {
        var vm = new ComponentTreeViewModel();
        vm.Add(Grid(10, 10, 5), "Base");
        vm.BrushRadiusMm = 1;
        Assert.False(vm.Sculpt(SculptTool.Inflate, 500, 500));
    }
}
