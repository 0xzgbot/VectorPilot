namespace VectorPilot.Serial;

/// <summary>Machine controller family (SPK-0415: drives post auto-select).</summary>
public enum MachineType { Grbl, Universal }

/// <summary>Units for the post-processed output (SPK-0415: drives the modal).</summary>
public enum MachineUnits { Millimeter, Inch }

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

    // SPK-0415: post auto-select surface (legacy JSON without these keys
    // deserializes to the defaults — Grbl + Millimeter).
    public MachineType MachineType { get; set; } = MachineType.Grbl;
    public MachineUnits Units { get; set; } = MachineUnits.Millimeter;

    /// <summary>Reflects MachineType — the bridge switches on this (SPK-0415).</summary>
    public string AutoPostProcessorType => MachineType == MachineType.Universal ? "universal" : "grbl";

    public bool IsSimulator => string.Equals(PortName, "SIMULATOR", StringComparison.OrdinalIgnoreCase);

    public static MachineProfile Simulator() => new() { Name = "Simulator (virtual GRBL)", PortName = "SIMULATOR" };
}
