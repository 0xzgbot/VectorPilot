using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class DroModelTests
{
    [Fact]
    public void Formats_Status_Into_Readout()
    {
        var parsed = StatusParser.Parse("<Run|MPos:0.000,0.000,0.000|WPos:12.3456,-3.2,0|FS:1500,16000|Ov:100,100,100>")!;
        var dro = DroModel.From(parsed);
        Assert.Equal("Run", dro.State);
        Assert.Equal("12.346", dro.X);
        Assert.Equal("-3.200", dro.Y);
        Assert.Equal("0.000", dro.Z);
        Assert.Equal("1500", dro.Feed);
        Assert.Equal("16000", dro.Spindle);
        Assert.True(dro.IsRunning);
        Assert.Contains("Run  X 12.346  Y -3.200", dro.Readout);
    }

    [Fact]
    public void Hold_State_Detected()
    {
        var parsed = StatusParser.Parse("<Hold|MPos:0.000,0.000,0.000>")!;
        Assert.True(DroModel.From(parsed).IsHeld);
    }

    [Fact]
    public void Overrides_Parse_And_Clamp()
    {
        var dro = new DroModel();
        dro.ApplyOverride("M220 S80");
        dro.ApplyOverride("M221 S250"); // clamps to 200
        Assert.Equal(80, dro.FeedOverridePercent);
        Assert.Equal(200, dro.SpindleOverridePercent);
    }

    [Fact]
    public void Unknown_Line_Ignored()
    {
        var dro = new DroModel();
        dro.ApplyOverride("G0 X10");
        Assert.Equal(100, dro.FeedOverridePercent);
    }
}
