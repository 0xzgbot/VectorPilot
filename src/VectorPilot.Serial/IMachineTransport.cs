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
}
