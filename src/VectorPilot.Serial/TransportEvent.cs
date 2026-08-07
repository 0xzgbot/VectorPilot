namespace VectorPilot.Serial;

public enum TransportEventType
{
    Opened,
    Closed,
    DataReceived,   // raw RX line
    Status,         // parsed status line "<Idle|MPos:...>"
    Ok,
    Error,          // "error:N" from controller
    Alarm,          // "ALARM:N" / "ALARM:"
    Message,        // "[MSG:...]" / "$I" report
    StreamProgress, // payload = "currentLine/totalLines"
    StreamComplete,
    StreamCancelled,
    ConnectionError
}

public sealed record TransportEvent(TransportEventType Type, string Payload, DateTime Time)
{
    public static TransportEvent Of(TransportEventType type, string payload = "") => new(type, payload, DateTime.Now);
    public override string ToString() => $"[{Time:HH:mm:ss.fff}] {Type}: {Payload}";
}

public enum MachineState
{
    Unknown,
    Idle,
    Run,
    Hold,
    Jog,
    Alarm,
    Door,
    Check,
    Home
}

/// <summary>Parsed GRBL status (subset of the Swift StatusParser contract).</summary>
public sealed class ParsedStatus
{
    public MachineState State { get; set; } = MachineState.Unknown;
    public VectorPilot.Geometry.VectorPoint? MPos { get; set; }
    public VectorPilot.Geometry.VectorPoint? WPos { get; set; }
    public double? FeedRate { get; set; }
    public double? SpindleSpeed { get; set; }
    public string Raw { get; set; } = "";
    public override string ToString() => $"{State} | MPos:{MPos?.ToString() ?? "-"} | WPos:{WPos?.ToString() ?? "-"} | FS:{FeedRate ?? 0},{SpindleSpeed ?? 0}";
}
