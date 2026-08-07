namespace VectorPilot.Serial;

/// <summary>Connection profile for a machine (mirrors ShopPilot MachineProfile).</summary>
public sealed class MachineProfile
{
    public string Name { get; set; } = "Desktop 12x24 (G-code)";
    public string PortName { get; set; } = "SIMULATOR";
    public int BaudRate { get; set; } = 115200;
    public double MaxX { get; set; } = 12;
    public double MaxY { get; set; } = 24;
    public double MaxZ { get; set; } = 4;
    public bool HasSpindle { get; set; } = true;
    public bool SupportsRotary { get; set; }
    public bool HasLaser { get; set; }

    public bool IsSimulator => string.Equals(PortName, "SIMULATOR", StringComparison.OrdinalIgnoreCase);

    public static MachineProfile Simulator() => new() { Name = "Simulator (virtual GRBL)", PortName = "SIMULATOR" };
}
