using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Machine-safety paths: E-stop hold/resume and jog position updates on the
/// simulator transport (the maximum verifiable coverage without hardware).
/// </summary>
public class MachineSafetyTests
{
    private static string[] LongProgram()
    {
        var lines = new List<string> { "G21", "G90" };
        for (int i = 0; i < 60; i++) lines.Add($"G1 X{i % 10} Y{(i * 7) % 10} F1000");
        lines.Add("M30");
        return lines.ToArray();
    }

    [Fact]
    public async Task Estop_Holds_State_And_Freezes_Stream_Then_Resumes()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        var states = new List<string>();
        var statesLock = new object();
        sim.EventReceived += evt =>
        {
            if (evt.Type == TransportEventType.Status && evt.Payload.Contains("MPos"))
            {
                var state = evt.Payload.Split('|')[0].Trim('<', '>');
                lock (statesLock) states.Add(state);
            }
        };

        string[] Snapshot() { lock (statesLock) return states.ToArray(); }

        var streamer = new GCodeStreamer(sim);
        var run = streamer.StartAsync(LongProgram());
        await Task.Delay(150);

        await sim.PauseAsync();               // E-stop (!)
        await Task.Delay(100);
        Assert.Contains("Hold", Snapshot());

        int frozen = streamer.CurrentLine;
        await Task.Delay(200);
        // At most one in-flight line can ack after the hold engages.
        Assert.True(streamer.CurrentLine - frozen <= 2, $"advanced {streamer.CurrentLine - frozen} while held");

        await sim.ResumeAsync();              // resume (~)
        await run;
        await Task.Delay(300);

        Assert.Equal(StreamPhase.Completed, streamer.Phase);
        Assert.Contains("Run", Snapshot());
    }

    [Fact]
    public async Task Jog_Relative_Moves_Position_By_Delta()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        await sim.JogAsync(10, 5, 0, 500); // $J=G91X10Y5F500 (relative)
        var status = await ReadStatus(sim);

        Assert.Equal(10, status.X, 3);
        Assert.Equal(5, status.Y, 3);
    }

    [Fact]
    public async Task Jog_Absolute_Sets_Exact_Position()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        await sim.JogAsync(25, -8, 3, 500);
        var status = await ReadStatus(sim);

        Assert.Equal(25, status.X, 3);
        Assert.Equal(-8, status.Y, 3);
        Assert.Equal(3, status.Z, 3);
    }

    private static async Task<(double X, double Y, double Z)> ReadStatus(SimulatorTransport sim)
    {
        var tcs = new TaskCompletionSource<(double, double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(TransportEvent evt)
        {
            if (evt.Type == TransportEventType.Status && evt.Payload.Contains("MPos"))
            {
                var mpos = evt.Payload.Split('|').FirstOrDefault(p => p.StartsWith("MPos"));
                if (mpos is null) return;
                var nums = mpos.Replace("MPos:", "").Split(',');
                if (nums.Length >= 3 && double.TryParse(nums[0], out var x) && double.TryParse(nums[1], out var y) && double.TryParse(nums[2], out var z))
                {
                    sim.EventReceived -= Handler;
                    tcs.TrySetResult((x, y, z));
                }
            }
        }
        sim.EventReceived += Handler;
        await sim.WriteLineAsync("?"); // poll status
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
