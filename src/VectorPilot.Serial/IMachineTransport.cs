namespace VectorPilot.Serial;

/// <summary>
/// Transport protocol (mirrors the ShopPilot MachineTransport protocol).
/// Implementations: SimulatorTransport (virtual GRBL) and SerialTransport (System.IO.Ports).
/// </summary>
public interface IMachineTransport : IAsyncDisposable
{
    event Action<TransportEvent>? EventReceived;
    bool IsOpen { get; }
    string Name { get; }

    Task OpenAsync(MachineProfile profile, CancellationToken ct = default);
    Task CloseAsync();
    /// <summary>Send one line (without newline). Async fire-and-forget with ack surfaced via events.</summary>
    Task WriteLineAsync(string line, CancellationToken ct = default);

    /// <summary>GRBL feed override (M220 S&lt;percent&gt;, 10-200).</summary>
    Task SetFeedOverrideAsync(int percent, CancellationToken ct = default);
    /// <summary>GRBL spindle override (M221 S&lt;percent&gt;, 10-200).</summary>
    Task SetSpindleOverrideAsync(int percent, CancellationToken ct = default);
    /// <summary>Cycle pause (GRBL realtime '!') — alias of the streamer's hold.</summary>
    Task PauseAsync(CancellationToken ct = default);
    /// <summary>Cycle resume (GRBL realtime '~').</summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>GRBL jog ($J=G91X..Y..Z..F.., relative).</summary>
    Task JogAsync(double x, double y, double z, double rate, CancellationToken ct = default);
}
