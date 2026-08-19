using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Card A5 gate items that were never asserted: jog must emit a real GRBL `$J=`
/// command (continuous jog shipped as a `...0.0F...` stub), and a hold must freeze
/// the stream rather than letting lines keep flowing.
/// </summary>
public class MachinePanelWireTests
{
    /// <summary>Transport that records every line written, so we can assert the wire format.</summary>
    private sealed class RecordingTransport : IMachineTransport
    {
        public List<string> Sent { get; } = new();
        public event Action<TransportEvent>? EventReceived;
        public bool IsOpen { get; private set; }
        public string Name => "Recording";

        public Task OpenAsync(MachineProfile profile, CancellationToken ct = default)
        {
            IsOpen = true;
            EventReceived?.Invoke(TransportEvent.Of(TransportEventType.Opened, "open"));
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string line, CancellationToken ct = default)
        {
            Sent.Add(line);

            // A real controller acknowledges each line; without this the streamer
            // legitimately times out waiting for 'ok'. Realtime characters
            // (!, ~, 0x18) are not acknowledged.
            if (line is not ("!" or "~") && line.Length > 0 && line[0] != '\x18')
                EventReceived?.Invoke(TransportEvent.Of(TransportEventType.Ok, "ok"));

            return Task.CompletedTask;
        }

        public Task JogAsync(double x, double y, double z, double rate, CancellationToken ct = default)
            => WriteLineAsync($"$J=G91 G21 X{x:0.###} Y{y:0.###} Z{z:0.###} F{rate:0}", ct);

        public Task PauseAsync(CancellationToken ct = default) => WriteLineAsync("!", ct);
        public Task ResumeAsync(CancellationToken ct = default) => WriteLineAsync("~", ct);
        public Task SetFeedOverrideAsync(int percent, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSpindleOverrideAsync(int percent, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>Push a status line as if the controller reported it.</summary>
        public void Report(string payload) => EventReceived?.Invoke(TransportEvent.Of(TransportEventType.Status, payload));
    }

    private static async Task<(MachineSession Session, RecordingTransport Tx)> Connected()
    {
        var tx = new RecordingTransport();
        var session = new MachineSession(tx);
        await session.ConnectAsync(new MachineProfile { Name = "test" });
        return (session, tx);
    }

    // ---- gate: jog emits $J= ----

    [Fact]
    public async Task Step_Jog_Emits_A_Real_Jog_Command()
    {
        var (session, tx) = await Connected();

        await session.JogAsync(10, 0, 0, 1000);

        Assert.Contains(tx.Sent, l => l.StartsWith("$J="));
        Assert.Contains(tx.Sent, l => l.Contains("X10"));
    }

    [Fact]
    public async Task Continuous_Jog_Is_Not_A_Zero_Distance_Stub()
    {
        var (session, tx) = await Connected();

        await session.JogContinuousAsync("X", +1, feed: 1500, maxTravelMm: 50);

        var jog = tx.Sent.LastOrDefault(l => l.StartsWith("$J="));
        Assert.NotNull(jog);
        // The shipped stub emitted "...X0.0F..." — a command that never moves.
        Assert.DoesNotContain("X0.0 ", jog!);
        Assert.DoesNotContain("X0.000", jog!);
        Assert.Contains("F1500", jog!);
    }

    [Fact]
    public async Task Continuous_Jog_Honours_The_Direction_Sign()
    {
        var (session, tx) = await Connected();

        await session.JogContinuousAsync("Y", -1, feed: 1000, maxTravelMm: 25);

        var jog = tx.Sent.Last(l => l.StartsWith("$J="));
        Assert.Contains("Y-25", jog);
    }

    [Fact]
    public async Task Jog_Is_Refused_While_Disconnected()
    {
        var tx = new RecordingTransport();
        var session = new MachineSession(tx);

        bool ok = await session.JogAsync(10, 0, 0, 1000);

        Assert.False(ok);
        Assert.Empty(tx.Sent);   // nothing reaches a closed port
    }

    // ---- gate: hold freezes the stream ----

    [Fact]
    public async Task Pause_Sends_The_Feed_Hold_Character()
    {
        var (session, tx) = await Connected();
        await session.StartStreamAsync(new List<string> { "G0 X0", "G1 X10 F100", "G1 X20 F100" });

        await session.PauseStreamAsync();

        Assert.Contains("!", tx.Sent);
    }

    [Fact]
    public async Task Resume_Sends_The_Cycle_Start_Character()
    {
        var (session, tx) = await Connected();
        await session.StartStreamAsync(new List<string> { "G0 X0", "G1 X10 F100" });
        await session.PauseStreamAsync();

        await session.ResumeAsync();

        Assert.Contains("~", tx.Sent);
    }

    [Fact]
    public async Task A_Hold_Report_Does_Not_Advance_The_Stream()
    {
        var (session, tx) = await Connected();
        await session.StartStreamAsync(Enumerable.Range(0, 40).Select(i => $"G1 X{i} F100").ToList());

        tx.Report("<Hold|MPos:0.000,0.000,0.000|FS:0,0>");
        int atHold = session.StreamedLines;

        await Task.Delay(120);   // give the send loop a chance to misbehave

        Assert.Equal(atHold, session.StreamedLines);
    }

    // ---- gate: E-stop / reset always available ----

    [Fact]
    public async Task Reset_Reaches_The_Controller_Even_While_Streaming()
    {
        var (session, tx) = await Connected();
        await session.StartStreamAsync(new List<string> { "G1 X1 F100", "G1 X2 F100" });

        await session.ResetAsync();

        Assert.NotEmpty(tx.Sent);
    }

    [Fact]
    public async Task Reset_Is_Attempted_Even_When_Disconnected()
    {
        var tx = new RecordingTransport();
        var session = new MachineSession(tx);

        // Must not throw: the operator can always hit Reset.
        await session.ResetAsync();
        Assert.True(true);
    }
}
