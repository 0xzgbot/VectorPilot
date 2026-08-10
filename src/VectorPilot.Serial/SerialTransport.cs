using System.IO.Ports;

namespace VectorPilot.Serial;

/// <summary>Real serial transport over System.IO.Ports (Win32 COM). GRBL line protocol.</summary>
public sealed class SerialTransport : IMachineTransport
{
    public event Action<TransportEvent>? EventReceived;
    public bool IsOpen { get; private set; }
    public string Name => "Serial (System.IO.Ports)";

    private SerialPort? _port;
    private readonly object _lock = new();

    public Task OpenAsync(MachineProfile profile, CancellationToken ct = default)
    {
        try
        {
            _port = new SerialPort(profile.PortName, profile.BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 500,
                WriteTimeout = 2000,
                NewLine = "\n"
            };
            _port.DataReceived += (_, _) =>
            {
                try
                {
                    string data = _port.ReadExisting();
                    foreach (var raw in data.Split('\n'))
                    {
                        var line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;
                        Emit(TransportEventType.DataReceived, line);
                        RouteInbound(line);
                    }
                }
                catch (Exception ex)
                {
                    Emit(TransportEventType.ConnectionError, $"read error: {ex.Message}");
                }
            };
            _port.Open();
            IsOpen = true;
            Emit(TransportEventType.Opened, $"Serial opened {profile.PortName} @ {profile.BaudRate}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsOpen = false;
            Emit(TransportEventType.ConnectionError, $"open failed: {ex.Message}");
            throw;
        }
    }

    private void RouteInbound(string line)
    {
        if (line.StartsWith("<")) Emit(TransportEventType.Status, line);
        else if (line.StartsWith("ALARM")) Emit(TransportEventType.Alarm, line);
        else if (line.StartsWith("error")) Emit(TransportEventType.Error, line);
        else if (line.StartsWith("ok")) Emit(TransportEventType.Ok, line);
        else if (line.StartsWith("[")) Emit(TransportEventType.Message, line);
    }

    public Task WriteLineAsync(string line, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_port is { IsOpen: true })
            {
                Emit(TransportEventType.DataReceived, line); // TX echo for the console
                _port.WriteLine(line);
            }
        }
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        lock (_lock)
        {
            if (_port is { IsOpen: true }) _port.Close();
            _port?.Dispose();
            _port = null;
        }
        IsOpen = false;
        Emit(TransportEventType.Closed, "Serial closed");
        return Task.CompletedTask;
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

    public Task JogAsync(double x, double y, double z, double rate, CancellationToken ct = default)
        => WriteLineAsync($"$J=G91X{x:0.###}Y{y:0.###}Z{z:0.###}F{(int)rate}", ct);
}
