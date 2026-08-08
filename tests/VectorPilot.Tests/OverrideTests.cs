using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

public class OverrideTests
{
    [Fact]
    public async Task FeedOverride_Sends_M220_And_Acks()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(new MachineProfile { Name = "Test" });

        var received = new List<string>();
        sim.EventReceived += e => { if (e.Type == TransportEventType.DataReceived) received.Add(e.Payload); };

        await sim.SetFeedOverrideAsync(80);
        Assert.Contains("M220 S80", received);

        // Simulator acks the override command.
        var acks = new List<string>();
        sim.EventReceived += e => { if (e.Type == TransportEventType.Ok) acks.Add(e.Payload); };
        await sim.SetFeedOverrideAsync(120);
        Assert.Contains("ok", acks);
    }

    [Fact]
    public async Task SpindleOverride_Clamps_And_Sends()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(new MachineProfile { Name = "Test" });

        var received = new List<string>();
        sim.EventReceived += e => { if (e.Type == TransportEventType.DataReceived) received.Add(e.Payload); };

        await sim.SetSpindleOverrideAsync(300); // clamped to 200
        await sim.SetSpindleOverrideAsync(5);   // clamped to 10
        Assert.Contains("M221 S200", received);
        Assert.Contains("M221 S10", received);
    }

    [Fact]
    public async Task Pause_And_Resume_Send_Cycle_Commands()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(new MachineProfile { Name = "Test" });

        var sent = new List<string>();
        sim.EventReceived += e => { if (e.Type == TransportEventType.DataReceived) sent.Add(e.Payload); };

        await sim.PauseAsync();
        await sim.ResumeAsync();
        Assert.Contains("!", sent);
        Assert.Contains("~", sent);
    }
}
