using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Weave must produce a real interlaced SURFACE, not just a volume estimate.
/// SweepExtrudeWeaveEngine.Weave returns VolumeMm3/SurfaceAreaMm2 and no geometry;
/// WeaveReliefGenerator produces the machinable heightfield.
/// </summary>
public class WeaveReliefTests
{
    private static WeaveParams P(WeavePattern pattern = WeavePattern.Plain) => new()
    {
        Pattern = pattern,
        WarpCount = 8,
        WeftCount = 8,
        ThreadSize = 4.0,
        Overlap = 0.5
    };

    [Fact]
    public void Generates_A_Populated_Heightfield()
    {
        var hf = WeaveReliefGenerator.Generate(P(), width: 40, height: 40, cellSizeMm: 0.5);

        Assert.Equal(80, hf.Width);
        Assert.Equal(80, hf.Height);
        Assert.True(hf.MaxHeight > 0, "the weave must have raised threads");
    }

    [Fact]
    public void Surface_Undulates_Rather_Than_Being_Flat()
    {
        var hf = WeaveReliefGenerator.Generate(P(), 40, 40, 0.5, threadHeight: 2.0);

        // A real weave has both crowns and valleys.
        Assert.True(hf.MaxHeight > 1.0, $"expected crowns near 2.0, got {hf.MaxHeight:F3}");
        Assert.True(hf.MinHeight < 0.5, $"expected valleys near 0, got {hf.MinHeight:F3}");
        Assert.True(hf.MaxHeight - hf.MinHeight > 1.0, "surface must undulate");
    }

    [Fact]
    public void Crowns_Do_Not_Exceed_The_Thread_Height()
    {
        var hf = WeaveReliefGenerator.Generate(P(), 40, 40, 0.5, threadHeight: 3.0);
        Assert.True(hf.MaxHeight <= 3.0 + 1e-6, $"max {hf.MaxHeight:F3} exceeds thread height 3.0");
    }

    [Fact]
    public void Plain_Weave_Alternates_Every_Crossing()
    {
        // 1/1: neighbours in either direction must swap which thread is on top.
        Assert.True(WeaveReliefGenerator.WarpIsOver(WeavePattern.Plain, 0, 0));
        Assert.False(WeaveReliefGenerator.WarpIsOver(WeavePattern.Plain, 1, 0));
        Assert.False(WeaveReliefGenerator.WarpIsOver(WeavePattern.Plain, 0, 1));
        Assert.True(WeaveReliefGenerator.WarpIsOver(WeavePattern.Plain, 1, 1));
    }

    [Fact]
    public void Twill_Floats_Two_Before_Switching()
    {
        // 2/2 diagonal: two over, then two under.
        Assert.True(WeaveReliefGenerator.WarpIsOver(WeavePattern.Twill, 0, 0));
        Assert.True(WeaveReliefGenerator.WarpIsOver(WeavePattern.Twill, 1, 0));
        Assert.False(WeaveReliefGenerator.WarpIsOver(WeavePattern.Twill, 2, 0));
        Assert.False(WeaveReliefGenerator.WarpIsOver(WeavePattern.Twill, 3, 0));
    }

    [Fact]
    public void Patterns_Produce_Different_Surfaces()
    {
        var plain = WeaveReliefGenerator.Generate(P(WeavePattern.Plain), 40, 40, 0.5);
        var twill = WeaveReliefGenerator.Generate(P(WeavePattern.Twill), 40, 40, 0.5);

        bool differs = false;
        for (int i = 0; i < plain.Heights.Length && !differs; i++)
            if (Math.Abs(plain.Heights[i] - twill.Heights[i]) > 1e-9) differs = true;

        Assert.True(differs, "plain and twill must not generate identical surfaces");
    }

    [Fact]
    public void Thread_Counts_Change_The_Pitch()
    {
        var coarse = WeaveReliefGenerator.Generate(
            new WeaveParams { WarpCount = 4, WeftCount = 4, ThreadSize = 4, Overlap = .5 }, 40, 40, 0.5);
        var fine = WeaveReliefGenerator.Generate(
            new WeaveParams { WarpCount = 16, WeftCount = 16, ThreadSize = 4, Overlap = .5 }, 40, 40, 0.5);

        // Finer weaves cross more often, so more cells sit at a crown.
        int coarseCrowns = coarse.Heights.Count(h => h > coarse.MaxHeight * 0.9);
        int fineCrowns = fine.Heights.Count(h => h > fine.MaxHeight * 0.9);
        Assert.NotEqual(coarseCrowns, fineCrowns);
    }

    [Fact]
    public void Estimator_Still_Reports_Volume_For_The_Same_Params()
    {
        // The estimator remains valid for cost/time; it just is not geometry.
        var r = SweepExtrudeWeaveEngine.Weave(Guid.NewGuid(), P(), 40, 40);
        Assert.True(r.Success);
        Assert.True(r.VolumeMm3 > 0);
    }
}
