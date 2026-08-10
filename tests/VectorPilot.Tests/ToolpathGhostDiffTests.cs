using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathGhostDiffTests
{
    [Fact]
    public void SetResult_Snapshots_Previous_On_Change()
    {
        var tp = new Toolpath { Name = "P1" };
        tp.SetResult(new[] { "G0 X0", "G1 X10" });
        Assert.Null(tp.PreviousGCode); // first gen: no previous

        tp.SetResult(new[] { "G0 X0", "G1 X20" });
        Assert.NotNull(tp.PreviousGCode);
        Assert.Equal(new[] { "G0 X0", "G1 X10" }, tp.PreviousGCode);
        Assert.False(tp.IsDirty);
    }

    [Fact]
    public void Noop_Regen_Keeps_Previous_Meaningful()
    {
        var tp = new Toolpath();
        tp.SetResult(new[] { "A", "B" });
        tp.SetResult(new[] { "C" });
        var ghost = tp.PreviousGCode!.ToList();
        tp.SetResult(new[] { "C" }); // no-op regen
        Assert.Equal(ghost, tp.PreviousGCode); // ghost not clobbered
    }

    [Fact]
    public void Param_Values_For_Job_Sheet()
    {
        var tp = new Toolpath { ParamFeedRate = 1000, ParamCutDepth = 12.5 };
        Assert.Equal(1000, tp.ParamFeedRate);
        Assert.Equal(12.5, tp.ParamCutDepth);
    }
}
