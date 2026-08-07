using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class StatusParserTests
{
    [Fact]
    public void Parses_Basic_Status()
    {
        var p = StatusParser.Parse("<Idle|MPos:1.234,5.678,0.000|WPos:1.234,5.678,0.000|FS:100,12000>");
        Assert.NotNull(p);
        Assert.Equal("Idle", p!.State);
        Assert.Equal(1.234, p.MPosX, 3);
        Assert.Equal(5.678, p.MPosY, 3);
        Assert.Equal(0.0, p.MPosZ, 3);
        Assert.NotNull(p.FS);
        Assert.Equal(100, p.FS!.Value.Feed, 1);
        Assert.Equal(12000, p.FS.Value.Spindle, 1);
    }

    [Fact]
    public void Parses_Run_State_And_Buffer()
    {
        var p = StatusParser.Parse("<Run|MPos:0.000,0.000,0.000|Bf:15,127>");
        Assert.NotNull(p);
        Assert.Equal("Run", p!.State);
        Assert.Equal(15, p.Buffer);
    }

    [Fact]
    public void Rejoins_Fragmented_Pn_Field()
    {
        // Pn contains pipes: Pn:000|0|0000 — the raw line splits into <..., Pn:000, 0, 0000>
        var p = StatusParser.Parse("<Idle|MPos:1.000,2.000,3.000|Pn:000|0|0000>");
        Assert.NotNull(p);
        Assert.NotNull(p!.Pins);
        Assert.Equal(3, p.Pins!.Limits.Length);
        Assert.Equal(0, p.Pins.Probe);
        Assert.Equal(4, p.Pins.Controls.Length);
    }

    [Fact]
    public void Rejects_Non_Status_Text()
    {
        Assert.Null(StatusParser.Parse("ok"));
        Assert.Null(StatusParser.Parse("ALARM:9"));
    }

    [Fact]
    public void Parses_Metric_Coordinates_With_Unit_Suffix()
    {
        var p = StatusParser.Parse("<Idle|MPos:10mm,20mm,5mm|WPos:10.0,20.0,5.0>");
        Assert.NotNull(p);
        Assert.Equal(10.0, p!.MPosX, 3);
        Assert.Equal(20.0, p.MPosY, 3);
    }

    [Fact]
    public void Parses_Alarm_State()
    {
        var p = StatusParser.Parse("<Alarm|MPos:0.000,0.000,0.000>");
        Assert.NotNull(p);
        Assert.Equal("Alarm", p!.State);
    }
}
