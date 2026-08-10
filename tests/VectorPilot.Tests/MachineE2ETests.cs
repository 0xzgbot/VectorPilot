using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Machine-control E2E loopback (hardening): simulator transport + streamer
/// through a full cycle — stream a program, count acks, pause/resume, finish.
/// </summary>
public class MachineE2ETests
{
    private static readonly string[] Program =
    {
        "G21", "G90", "G0 X0 Y0", "G1 X10 Y0 F1000", "G1 X10 Y10 F1000",
        "G1 X0 Y10 F1000", "G1 X0 Y0 F1000", "G0 Z5", "M5", "M30"
    };

    /// <summary>Longer program so pause lands mid-stream deterministically.</summary>
    private static string[] LongProgram()
    {
        var lines = new List<string> { "G21", "G90" };
        for (int i = 0; i < 60; i++) lines.Add($"G1 X{i % 10} Y{(i * 7) % 10} F1000");
        lines.Add("M30");
        return lines.ToArray();
    }

    [Fact]
    public async Task Stream_Full_Program_To_Simulator()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        int oks = 0;
        sim.EventReceived += evt => { if (evt.Type == TransportEventType.Ok) oks++; };

        var streamer = new GCodeStreamer(sim);
        var progress = new List<double>();
        streamer.ProgressChanged += p => progress.Add((double)p.CurrentLine / p.TotalLines);

        await streamer.StartAsync(Program);
        await Task.Delay(300); // let the virtual GRBL drain

        Assert.Equal(Program.Length, oks);
        Assert.Equal(Program.Length, streamer.CurrentLine);
        Assert.Equal(Program.Length, streamer.TotalLines);
        Assert.Equal(StreamPhase.Completed, streamer.Phase);
        Assert.Contains(progress, p => p > 0.5 && p < 1.0); // mid-stream progress observed
        Assert.Equal(1.0, progress[^1], 3);
    }

    [Fact]
    public async Task Pause_Resume_Does_Not_Lose_Lines()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        var streamer = new GCodeStreamer(sim);
        var run = streamer.StartAsync(LongProgram());
        await Task.Delay(120);
        streamer.Pause();
        int pausedLine = streamer.CurrentLine;
        Assert.Equal(StreamPhase.Paused, streamer.Phase);
        await Task.Delay(200);
        // At most the in-flight buffer drains while paused.
        Assert.True(streamer.CurrentLine - pausedLine <= 2, $"advanced {streamer.CurrentLine - pausedLine} while paused");
        streamer.Resume();
        await run;
        await Task.Delay(200);

        Assert.Equal(LongProgram().Length, streamer.CurrentLine);
        Assert.Equal(StreamPhase.Completed, streamer.Phase);
    }

    [Fact]
    public async Task Cancel_Stops_Mid_Stream()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        var streamer = new GCodeStreamer(sim);
        var run = streamer.StartAsync(LongProgram());
        await Task.Delay(120);
        streamer.Cancel();
        await run;

        Assert.True(streamer.CurrentLine < LongProgram().Length);
        Assert.Equal(StreamPhase.Cancelled, streamer.Phase);
    }
}
