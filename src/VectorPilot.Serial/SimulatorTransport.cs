using System.Text;

namespace VectorPilot.Serial;

/// <summary>
/// Virtual GRBL controller for development without hardware.
/// Responds like real GRBL: "ok" after commands, status lines on "?", jogs move the
/// virtual machine, $H homes, 0x18 (Ctrl-X) triggers ALARM, $X unlocks, M3/M5 spindle.
/// </summary>
public sealed class SimulatorTransport : IMachineTransport
{
    public event Action<TransportEvent>? EventReceived;
    public bool IsOpen { get; private set; }
    public string Name => "Simulator (virtual GRBL)";

    private MachineProfile _profile = MachineProfile.Simulator();
    private double _x, _y, _z;
    private MachineState _state = MachineState.Idle;
    private double _feedRate;
    private double _spindleSpeed = 12000;
    private readonly object _lock = new();

    public Task OpenAsync(MachineProfile profile, CancellationToken ct = default)
    {
        _profile = profile;
        IsOpen = true;
        _state = MachineState.Idle;
        Emit(TransportEventType.Opened, $"Simulator opened (profile {profile.Name}, baud {profile.BaudRate})");
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        Emit(TransportEventType.Closed, "Simulator closed");
        return Task.CompletedTask;
    }

    public async Task WriteLineAsync(string line, CancellationToken ct = default)
    {
        if (!IsOpen) return;
        line = line.TrimEnd('\r', '\n');
        Emit(TransportEventType.DataReceived, line);

        // Real-time commands (GRBL: ! ~ ? 0x18) bypass the line buffer.
        if (line == "?")
        {
            Emit(TransportEventType.Status, BuildStatusLine());
            return;
        }
        if (line.StartsWith("M220") || line.StartsWith("M221"))
        {
            // Feed/spindle override: accepted as a normal command → ok.
            Emit(TransportEventType.Ok, "ok");
            return;
        }
        if (line == "!")
        {
            if (_state == MachineState.Run) { _state = MachineState.Hold; Emit(TransportEventType.Status, BuildStatusLine()); }
            Emit(TransportEventType.Ok, "ok");
            return;
        }
        if (line == "~")
        {
            if (_state == MachineState.Hold) { _state = MachineState.Run; Emit(TransportEventType.Status, BuildStatusLine()); }
            Emit(TransportEventType.Ok, "ok");
            return;
        }
        if (line == "\u0018" || line == "\u0018\u0018") // 0x18 soft-reset
        {
            _state = MachineState.Alarm;
            Emit(TransportEventType.Alarm, "ALARM:9 Soft reset");
            return;
        }

        await Task.Delay(2, ct).ConfigureAwait(false); // simulate processing latency

        var upper = line.ToUpperInvariant();
        try
        {
            if (upper.StartsWith("$H"))
            {
                _x = _y = _z = 0;
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("$X"))
            {
                _state = MachineState.Idle;
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("G28") || upper.StartsWith("$HZ") || upper.StartsWith("G10 L20"))
            {
                Emit(TransportEventType.Ok, "ok");
            }
            else if (upper.StartsWith("G10"))
            {
                // G10 L20 P1 X.. Y.. Z..  -> set work offset (implement zeroing)
                Emit(TransportEventType.Ok, "ok");
            }
            else if (upper.StartsWith("M3") || upper.StartsWith("M4"))
            {
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("M5"))
            {
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("G0") || upper.StartsWith("G1") || upper.StartsWith("G2") || upper.StartsWith("G3"))
            {
                MoveMachine(line);
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("$J"))
            {
                // jog: $J=G91X10F100
                JogFrom(line);
                Emit(TransportEventType.Ok, "ok");
                Emit(TransportEventType.Status, BuildStatusLine());
            }
            else if (upper.StartsWith("F"))
            {
                _feedRate = ParseNumberAfter(line, 'F', _feedRate);
                Emit(TransportEventType.Ok, "ok");
            }
            else if (upper.StartsWith("S"))
            {
                _spindleSpeed = ParseNumberAfter(line, 'S', _spindleSpeed);
                Emit(TransportEventType.Ok, "ok");
            }
            else if (upper.StartsWith("M30") || upper.StartsWith("M2"))
            {
                Emit(TransportEventType.Ok, "ok");
            }
            else
            {
                Emit(TransportEventType.Ok, "ok"); // accept anything else like real GRBL
            }
        }
        catch (Exception ex)
        {
            Emit(TransportEventType.Error, $"error: simulator exception {ex.Message}");
        }
    }

    private void MoveMachine(string line)
    {
        double targetX = _x, targetY = _y, targetZ = _z;
        var upper = line.ToUpperInvariant();
        targetX = ParseNumberAfter(upper, 'X', targetX);
        targetY = ParseNumberAfter(upper, 'Y', targetY);
        targetZ = ParseNumberAfter(upper, 'Z', targetZ);
        bool absolute = !upper.Contains("G91");
        if (absolute) { _x = targetX; _y = targetY; _z = targetZ; }
        else { _x += targetX; _y += targetY; _z += targetZ; }
    }

    private void JogFrom(string line)
    {
        // $J=G91X10F100 or $J=G90X5Y5
        var body = line.Split('=').Last();
        bool absolute = body.ToUpperInvariant().Contains("G90");
        double dx = ParseNumberAfter(body.ToUpperInvariant(), 'X', 0);
        double dy = ParseNumberAfter(body.ToUpperInvariant(), 'Y', 0);
        double dz = ParseNumberAfter(body.ToUpperInvariant(), 'Z', 0);
        if (absolute) { _x = dx; _y = dy; _z = dz; }
        else { _x += dx; _y += dy; _z += dz; }
    }

    private string BuildStatusLine()
    {
        string state = _state switch
        {
            MachineState.Run => "Run",
            MachineState.Hold => "Hold",
            MachineState.Jog => "Jog",
            MachineState.Alarm => "Alarm",
            MachineState.Home => "Home",
            MachineState.Check => "Check",
            MachineState.Door => "Door",
            _ => "Idle"
        };
        return $"<{state}|MPos:{_x:F3},{_y:F3},{_z:F3}|WPos:{_x:F3},{_y:F3},{_z:F3}|FS:{_feedRate:F0},{_spindleSpeed:F0}|Ov:100,100,100>";
    }

    private static double ParseNumberAfter(string s, char code, double fallback)
    {
        int idx = s.IndexOf(code);
        if (idx < 0 || idx + 1 >= s.Length) return fallback;
        var num = new StringBuilder();
        int i = idx + 1;
        if (s[i] == '-' || s[i] == '+') { num.Append(s[i]); i++; }
        bool any = false;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
        {
            num.Append(s[i]); i++; any = true;
        }
        return any && double.TryParse(num.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private void Emit(TransportEventType type, string payload) => EventReceived?.Invoke(TransportEvent.Of(type, payload));

    public async ValueTask DisposeAsync() => await CloseAsync();

    // ---- Overrides / cycle control (GRBL 1.1) ----

    public Task SetFeedOverrideAsync(int percent, CancellationToken ct = default)
        => WriteLineAsync($"M220 S{Math.Clamp(percent, 10, 200)}", ct);

    public Task SetSpindleOverrideAsync(int percent, CancellationToken ct = default)
        => WriteLineAsync($"M221 S{Math.Clamp(percent, 10, 200)}", ct);

    public Task PauseAsync(CancellationToken ct = default) => WriteLineAsync("!", ct);

    public Task ResumeAsync(CancellationToken ct = default) => WriteLineAsync("~", ct);
}
