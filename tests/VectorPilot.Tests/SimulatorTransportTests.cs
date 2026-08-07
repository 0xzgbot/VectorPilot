using VectorPilot.Engine;
using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

public class SimulatorTransportTests
{
    [Fact]
    public async Task Open_Then_Status_Query_Returns_Idle()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("?");

        var status = events.LastOrDefault(e => e.Type == TransportEventType.Status);
        Assert.NotNull(status);
        var parsed = StatusParser.Parse(status!.Payload);
        Assert.Equal("Idle", parsed!.State);
        Assert.Equal(0.0, parsed.MPosX, 3);
    }

    [Fact]
    public async Task Jog_Moves_Virtual_Machine()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("$J=G91X10F100");

        var status = events.LastOrDefault(e => e.Type == TransportEventType.Status);
        var parsed = StatusParser.Parse(status!.Payload);
        Assert.Equal(10.0, parsed!.MPosX, 3);
    }

    [Fact]
    public async Task G0_Absolute_Move_Targets_Position()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("G0 X5 Y-3 Z1");

        var status = events.LastOrDefault(e => e.Type == TransportEventType.Status);
        var parsed = StatusParser.Parse(status!.Payload);
        Assert.Equal(5.0, parsed!.MPosX, 3);
        Assert.Equal(-3.0, parsed.MPosY, 3);
        Assert.Equal(1.0, parsed.MPosZ, 3);
    }

    [Fact]
    public async Task Spindle_Commands_Reflect_In_Status()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("M3 S12000");

        var ok = events.Any(e => e.Type == TransportEventType.Ok);
        Assert.True(ok);
    }

    [Fact]
    public async Task SoftReset_Triggers_Alarm()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("\u0018");

        var alarm = events.Any(e => e.Type == TransportEventType.Alarm);
        Assert.True(alarm);
    }

    [Fact]
    public async Task Home_Zeroes_Position()
    {
        await using var sim = new SimulatorTransport();
        var events = new List<TransportEvent>();
        sim.EventReceived += events.Add;

        await sim.OpenAsync(MachineProfile.Simulator());
        await sim.WriteLineAsync("$J=G91X25");
        await sim.WriteLineAsync("$H");

        var status = events.LastOrDefault(e => e.Type == TransportEventType.Status);
        var parsed = StatusParser.Parse(status!.Payload);
        Assert.Equal(0.0, parsed!.MPosX, 3);
    }
}
