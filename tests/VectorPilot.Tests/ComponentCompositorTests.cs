using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ComponentOperationEngineTests
{
    private static HeightfieldData Grid(double[] heights, int w = 5, int h = 5)
        => new(w, h, 1.0, 0, 0, heights);

    [Fact]
    public void Smooth_Reduces_Spike()
    {
        var h = new double[25];
        Array.Fill(h, 2.0);
        h[12] = 10.0; // spike at center
        var r = ComponentOperationEngine.Smooth(Grid(h), new SmoothParams { Iterations = 3, SmoothingFactor = 0.5 });
        Assert.True(r.Heights[12] < 10.0);
        Assert.True(r.Heights[12] > 2.0);
    }

    [Fact]
    public void Smooth_PreserveVolume_Keeps_Mean()
    {
        var h = new double[25];
        for (int i = 0; i < 25; i++) h[i] = 1.0 + i * 0.1;
        var hf = Grid(h);
        double before = hf.Heights.Average();
        var r = ComponentOperationEngine.Smooth(hf, new SmoothParams { Iterations = 2, SmoothingFactor = 0.5, PreserveVolume = true });
        Assert.Equal(before, r.Heights.Average(), 6);
    }

    [Fact]
    public void Emboss_Raised_Adds_Dome()
    {
        var h = new double[25];
        Array.Fill(h, 1.0);
        var r = ComponentOperationEngine.Emboss(Grid(h), new EmbossParams { EmbossType = EmbossType.Raised, Depth = 2.0 });
        Assert.True(r.Heights[12] > 1.0); // center peaked
        Assert.Equal(1.0, r.Heights[0], 6); // corner untouched
    }

    [Fact]
    public void Emboss_Recessed_Clamps_At_Zero()
    {
        var h = new double[25];
        Array.Fill(h, 1.0);
        var r = ComponentOperationEngine.Emboss(Grid(h), new EmbossParams { EmbossType = EmbossType.Recessed, Depth = 5.0 });
        Assert.True(r.Heights[12] <= 1.0);
        Assert.All(r.Heights, v => Assert.True(v >= 0));
    }

    [Fact]
    public void Split_Keeps_Above_Rebased_To_Zero()
    {
        var h = new double[25];
        for (int i = 0; i < 25; i++) h[i] = i % 5 + 1; // rows 1..5
        var r = ComponentOperationEngine.Split(Grid(h), planeHeight: 3);
        Assert.All(r.Heights, v => Assert.True(v >= 0));
        Assert.Contains(r.Heights, v => v == 0); // below-plane cells
        Assert.True(r.MaxHeight <= 2.0);          // 5-3 = 2 rebased
    }
}

public class ComponentCompositorTests
{
    private static HeightfieldData Flat(double v, int w = 4, int h = 4)
    {
        var hh = new double[w * h];
        Array.Fill(hh, v);
        return new HeightfieldData(w, h, 1.0, 0, 0, hh);
    }

    [Fact]
    public void Composite_Add_Caps_At_Tallest_Input()
    {
        var parts = new List<ReliefComponent>
        {
            new(Flat(3)) { Name = "Base" },
            new(Flat(5)) { Name = "Raised" }
        };
        var r = ComponentCompositor.Composite(parts);
        Assert.NotNull(r);
        Assert.All(r!.Heights, v => Assert.Equal(5.0, v, 6)); // 3+5 capped at max 5
    }

    [Fact]
    public void Composite_Subtract_Clamps_Zero()
    {
        var parts = new List<ReliefComponent>
        {
            new(Flat(2)) { Name = "Base" },
            new(Flat(5)) { CombineMode = OperationMode.CombineSubtract }
        };
        var r = ComponentCompositor.Composite(parts);
        Assert.NotNull(r);
        Assert.All(r!.Heights, v => Assert.Equal(0.0, v, 6));
    }

    [Fact]
    public void Composite_Merge_Keeps_Higher()
    {
        var parts = new List<ReliefComponent>
        {
            new(Flat(2)) { Name = "Low" },
            new(Flat(4)) { CombineMode = OperationMode.CombineMerge }
        };
        var r = ComponentCompositor.Composite(parts);
        Assert.NotNull(r);
        Assert.All(r!.Heights, v => Assert.Equal(4.0, v, 6));
    }

    [Fact]
    public void Combine_Requires_Aligned_Grids()
    {
        var a = Flat(1, 4, 4);
        var b = Flat(2, 5, 4); // different width
        Assert.Null(ComponentCompositor.Combine(a, b, OperationMode.CombineAdd));
    }

    [Fact]
    public void Invisible_Components_Are_Skipped()
    {
        var parts = new List<ReliefComponent>
        {
            new(Flat(3)) { Name = "Base" },
            new(Flat(9)) { Visible = false }
        };
        var r = ComponentCompositor.Composite(parts);
        Assert.NotNull(r);
        Assert.All(r!.Heights, v => Assert.Equal(3.0, v, 6));
    }

    [Fact]
    public void Empty_Stack_Returns_Null()
    {
        Assert.Null(ComponentCompositor.Composite(new List<ReliefComponent>()));
    }
}
