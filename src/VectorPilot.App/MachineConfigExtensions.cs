using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App;

/// <summary>Builds a Serial-layer MachineProfile from an Engine config entry
/// (mm → inch conversion: the serial layer works in inches, G20).</summary>
public static class MachineConfigEntryExtensions
{
    public static MachineProfile ToProfile(this MachineConfigEntry entry)
        => new()
        {
            Name = entry.Name,
            PortName = entry.Port,
            MaxX = entry.TravelXmm / 25.4,
            MaxY = entry.TravelYmm / 25.4,
            MaxZ = entry.TravelZmm / 25.4,
            HasSpindle = true,
            SupportsRotary = entry.Axes >= 4
        };
}
