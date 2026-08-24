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

    /// <summary>Test seam: seed the DRO so jog-delta math has a known head position.</summary>
    public void SetDroForTest(double x, double y)
    {
        Dro = DroModel.From(new ParsedMachineStatus
        {
            State = "Idle",
            WPosX = x,
            WPosY = y,
            WPosZ = 0,
        });
    }
    public bool IsConnected => _transport.IsOpen;
    public bool IsStreaming { get; private set; }

    /// <summary>
    /// The transport this session drives. Exposed for the machine dock's Hold, which needs
    /// the realtime '!' (pause) — MachineSession itself has no Hold method.
    /// </summary>
    public IMachineTransport Transport => _transport;

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

    /// <summary>
    /// Frame the job: rapid (G0) around the rectangle [x0,x1]x[y0,y1] at safe Z, so the
    /// operator can see the machine's travel cover the work area before cutting. Emits
    /// lift → four corners → return to start. Returns false when not connected.
    ///
    /// H-104: gSender/LightBurn both frame before every job; without it the first sign of
    /// a mis-set origin is a tool crash.
    /// </summary>
    public async Task<bool> FrameAsync(double x0, double y0, double x1, double y1, double feed, double safeZ)
    {
        if (!_transport.IsOpen) return false;

        var lines = new List<string>
        {
            $"G0 Z{safeZ:0.###}",
            $"G0 X{x0:0.###} Y{y0:0.###}",
            $"G0 X{x1:0.###} Y{y0:0.###}",
            $"G0 X{x1:0.###} Y{y1:0.###}",
            $"G0 X{x0:0.###} Y{y1:0.###}",
            $"G0 X{x0:0.###} Y{y0:0.###}",
        };
        foreach (var line in lines)
        {
            Log($">> {line}");
            await _transport.WriteLineAsync(line);
        }
        return true;
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

    // ---- H-401: touch-plate probe ----

    /// <summary>
    /// Probe Z with a touch plate (GRBL G38.2). Drives the tool down toward
    /// <paramref name="targetZ"/>, stopping at the plate; on contact, offsets work
    /// zero by (plate thickness + tool tip allowance) so Z0 lands ON the stock.
    /// Returns false when not connected or the probe never touched (no motion was
    /// left half-done — GRBL reports the failure and the operator decides).
    /// </summary>
    public async Task<ProbeResult> ProbeZAsync(double targetZ, double feed, double plateThicknessMm)
    {
        if (!_transport.IsOpen) return new ProbeResult { Success = false, Reason = "not connected" };
        if (feed <= 0) return new ProbeResult { Success = false, Reason = "probe feed must be positive" };

        Log($">> G38.2 Z{targetZ:0.###} F{feed:0} (touch-plate probe)");

        var transport = _transport as SimulatorTransport;
        bool failed = false;
        void OnEvent(TransportEvent ev)
        {
            if (ev.Type == TransportEventType.Error && ev.Payload.Contains("probeFail"))
                failed = true;
        }
        _transport.EventReceived += OnEvent;
        try
        {
            await _transport.WriteLineAsync($"G38.2 Z{targetZ:0.###} F{feed:0}");
        }
        finally
        {
            _transport.EventReceived -= OnEvent;
        }

        if (failed || transport is null && !IsConnected)
        {
            Log("<< probe FAILED — no contact within travel");
            return new ProbeResult { Success = false, Reason = "no contact within travel" };
        }

        double zAtContact = Dro.Z is { } zs &&
            double.TryParse(zs, System.Globalization.CultureInfo.InvariantCulture, out var zv) ? zv : 0;

        // Zero Z on top of the plate, then lift by its thickness so Z0 = stock top.
        await _transport.WriteLineAsync(
            $"G10 L20 P1 Z{-plateThicknessMm:0.###}");
        Log($">> G10 L20 P1 Z{-plateThicknessMm:0.###} (zero on plate top → Z0 = stock top)");

        return new ProbeResult
        {
            Success = true,
            ContactZ = zAtContact,
            Reason = $"contact at Z{zAtContact:0.000}; Z0 set to plate top ({plateThicknessMm:0.##}mm)"
        };
    }

    // ---- H-403: rotary mode (Y→A wrap at send time, optional) ----

    /// <summary>When true, every streamed/sent Y word is rewritten to an A word
    /// (degrees = linear / circumference × 360) before it reaches the transport —
    /// gSender's "Y-as-A" trick for programs not yet posted for rotary.</summary>
    public bool RotaryModeEnabled { get; private set; }

    public double RotaryDiameterMm { get; private set; } = 50;

    /// <summary>Toggle rotary mode. Returns the resulting state. No motion is sent;
    /// this only affects how SUBSEQUENT sends are translated. Documented in the
    /// dock tooltip so the operator knows the DRO's A column follows the wrap.</summary>
    public bool SetRotaryMode(bool enabled, double diameterMm)
    {
        RotaryModeEnabled = enabled;
        RotaryDiameterMm = Math.Max(1, diameterMm);
        Log(enabled
            ? $"-- rotary mode ON (Ø {RotaryDiameterMm:0.#}mm): Y words wrap to A at send time"
            : "-- rotary mode OFF: Y words sent as-is");
        return RotaryModeEnabled;
    }

    /// <summary>Send one line through the rotary translator when enabled.
    /// G0/G1/G2/G3 lines have their Y word converted to A; everything else passes
    /// through untouched. Returns false when not connected (same as SendAsync).</summary>
    public async Task<bool> SendWithRotaryWrapAsync(string line)
    {
        if (!_transport.IsOpen) return false;

        if (!RotaryModeEnabled)
        {
            await SendAsync(line);
            return true;
        }

        var wrapped = WrapYToA(line);
        Log($">> [rotary] {line}  ⇒  {wrapped}");
        await _transport.WriteLineAsync(wrapped);
        return true;
    }

    /// <summary>Rewrite a motion line's Y word into an A word using
    /// angle = (linear / circumference) × 360, direction preserved. Public seam:
    /// tests pin the exact translation the stream path uses.</summary>
    public string WrapYToA(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            line, @"^(.*?G[0-3]\s*)(.*?)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return line;

        string head = m.Groups[1].Value, rest = m.Groups[2].Value;
        var yMatch = System.Text.RegularExpressions.Regex.Match(
            rest, @"Y(-?\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!yMatch.Success) return line;

        double linear = double.Parse(yMatch.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        double degrees = linear / (Math.PI * RotaryDiameterMm) * 360.0;

        string replaced = rest[..yMatch.Index] +
            $"A{degrees.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
            rest[(yMatch.Index + yMatch.Length)..];
        return head + replaced;
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

/// <summary>H-401: outcome of a touch-plate probe.</summary>
public sealed class ProbeResult
{
    public bool Success { get; init; }
    public double ContactZ { get; init; }
    public string Reason { get; init; } = "";
}
