using System.IO.Ports;

namespace VectorPilot.Serial;

public static class SerialPortEnumerator
{
    public static IReadOnlyList<string> EnumeratePorts()
    {
        try { return SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
