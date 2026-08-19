using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App;

/// <summary>
/// Card A5: machine-control session. Owns the transport, the DRO, and the
/// streamer, and enforces the ported safety invariants:
/// E-stop/Reset are ALWAYS available, streaming NEVER auto-starts, and a
/// disconnect mid-stream raises an alarm.
/// </summary>
public sealed class MachineSession : IAsyncDisposable
{
    private readonly IMachineTransport _transport;
    private GCodeStreamer? _streamer;

    public DroModel Dro { get; private set; } = new();
    public bool IsConnected => _transport.IsOpen;
    public bool IsStreaming { get; private set; }

    /// <summary>Raw TX/RX log for the console toggle (newest last).</summary>
    public List<string> ConsoleLog { get; } = new();
    public bool ConsoleEnabled { get; set; } = true;

    /// <summary>Raised when a safety condition trips (disconnect mid-stream, alarm line).</summary>
    public event Action<string>? Alarm;

    public MachineSession(IMachineTransport transport)
    {
        _transport = transport;
        _transport.EventReceived += OnTransportEvent;
    }

    // ---- connection ----

    public async Task<bool> ConnectAsync(MachineProfile profile)
    {
        if (_transport.IsOpen) return true;
        await _transport.OpenAsync(profile);
        Log($"-- connected: {profile.PortName}");
        return _transport.IsOpen;
    }

    public async Task DisconnectAsync()
    {
        bool wasStreaming = IsStreaming;
        if (wasStreaming) _streamer?.Cancel();
        IsStreaming = false;
        await _transport.CloseAsync();
        Log("-- disconnected");
        if (wasStreaming) Alarm?.Invoke("Disconnected while streaming — motion stopped.");
    }

    // ---- safety: always permitted, even when idle or disconnected ----

    /// <summary>E-stop. Never gated on state — the invariant is that it always works.</summary>
    public async Task EmergencyStopAsync()
    {
        Log(">> ! (feed hold / E-STOP)");
        if (_transport.IsOpen) await _transport.PauseAsync();
        _streamer?.Cancel();
        IsStreaming = false;
        Alarm?.Invoke("E-STOP engaged.");
    }

    /// <summary>Soft reset (GRBL Ctrl-X).</summary>
    public async Task ResetAsync()
    {
        Log(">> 0x18 (soft reset)");
        if (_transport.IsOpen) await _transport.WriteLineAsync("\x18");
        _streamer?.Cancel();
        IsStreaming = false;
    }

    public async Task ResumeAsync()
    {
        Log(">> ~ (cycle resume)");
        if (_transport.IsOpen) await _transport.ResumeAsync();
    }

    // ---- motion ----

    /// <summary>Continuous jog: a real distance in the requested direction, not a 0.0 no-op.</summary>
    public async Task<bool> JogContinuousAsync(string axis, double sign, double feed, double maxTravelMm = 1000)
    {
        if (!_transport.IsOpen) return false;
        double d = sign * maxTravelMm;
        double x = axis == "X" ? d : 0, y = axis == "Y" ? d : 0, z = axis == "Z" ? d : 0;
        Log($">> jog continuous {axis}{(sign > 0 ? "+" : "-")} F{feed}");
        await _transport.JogAsync(x, y, z, feed);
        return true;
    }

    /// <summary>Cancel an in-flight jog (GRBL 0x85).</summary>
    public async Task<bool> JogCancelAsync()
    {
        if (!_transport.IsOpen) return false;
        Log(">> 0x85 (jog cancel)");
        await _transport.WriteLineAsync("\x85");
        return true;
    }

    /// <summary>Raw line passthrough for panel buttons ($X, M3, G10 …).</summary>
    public async Task<bool> SendAsync(string line)
    {
        if (!_transport.IsOpen) return false;
        Log($">> {line}");
        await _transport.WriteLineAsync(line);
        return true;
    }

    /// <summary>Status poll ('?').</summary>
    public async Task PollAsync()
    {
        if (_transport.IsOpen) await _transport.WriteLineAsync("?");
    }

    public async Task<bool> JogAsync(double dx, double dy, double dz, double feed)
    {
        if (!_transport.IsOpen) return false;
        Log($">> jog X{dx} Y{dy} Z{dz} F{feed}");
        await _transport.JogAsync(dx, dy, dz, feed);
        return true;
    }

    public async Task<bool> SoftHomeAsync()
    {
        if (!_transport.IsOpen) return false;
        Log(">> $H (home)");
        await _transport.WriteLineAsync("$H");
        return true;
    }

    public async Task<bool> SetWorkZeroAsync()
    {
        if (!_transport.IsOpen) return false;
        Log(">> G10 L20 P1 X0 Y0 Z0 (work zero)");
        await _transport.WriteLineAsync("G10 L20 P1 X0 Y0 Z0");
        return true;
    }

    // ---- streaming (explicit consent only) ----

    /// <summary>The active streamer (null until a stream starts) — for progress binding.</summary>
    public GCodeStreamer? Streamer => _streamer;

    /// <summary>
    /// Start streaming. Requires an open port and an explicit call — nothing in
    /// this class starts a program on connect, load, or any event.
    /// </summary>
    public async Task<bool> StartStreamAsync(IReadOnlyList<string> lines)
    {
        if (!_transport.IsOpen || IsStreaming || lines.Count == 0) return false;
        _streamer = new GCodeStreamer(_transport);
        IsStreaming = true;
        Log($"-- stream start: {lines.Count} line(s)");
        await _streamer.StartAsync(lines);
        return true;
    }

    public int StreamedLines => _streamer?.CurrentLine ?? 0;
    public int TotalLines => _streamer?.TotalLines ?? 0;

    public async Task PauseStreamAsync()
    {
        if (_transport.IsOpen) await _transport.PauseAsync();
        Log(">> ! (pause)");
    }

    // ---- plumbing ----

    private void OnTransportEvent(TransportEvent ev)
    {
        if (ev.Type == TransportEventType.Status && StatusParser.Parse(ev.Payload) is { } parsed)
            Dro = DroModel.From(parsed);

        if (ConsoleEnabled) Log($"<< {ev.Payload}");

        if (ev.Payload.Contains("ALARM", StringComparison.OrdinalIgnoreCase))
        {
            IsStreaming = false;
            Alarm?.Invoke(ev.Payload);
        }
    }

    private void Log(string line)
    {
        if (!ConsoleEnabled) return;
        ConsoleLog.Add(line);
        if (ConsoleLog.Count > 500) ConsoleLog.RemoveRange(0, ConsoleLog.Count - 500);
    }

    public async ValueTask DisposeAsync()
    {
        _transport.EventReceived -= OnTransportEvent;
        if (_streamer is not null) await _streamer.DisposeAsync();
    }
}
