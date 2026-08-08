using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// DRO (digital readout) view-model over the Engine's ParsedMachineStatus:
/// formatted WPos/MPos readouts, state, feed/spindle, and override
/// percentages. Lives in App (Engine has the parser; Serial has the transport).
/// </summary>
public sealed class DroModel
{
    public string State { get; private set; } = "Idle";
    public string X { get; private set; } = "0.000";
    public string Y { get; private set; } = "0.000";
    public string Z { get; private set; } = "0.000";
    public string Feed { get; private set; } = "0";
    public string Spindle { get; private set; } = "0";
    public int FeedOverridePercent { get; private set; } = 100;
    public int SpindleOverridePercent { get; private set; } = 100;
    public bool IsRunning => State is "Run" or "Jog" or "Hold";
    public bool IsHeld => State == "Hold";

    public static DroModel From(ParsedMachineStatus status)
    {
        var dro = new DroModel
        {
            State = status.State,
            Feed = $"{status.FS?.Feed ?? 0:0}",
            Spindle = $"{status.FS?.Spindle ?? 0:0}"
        };
        dro.X = $"{status.WPosX:0.000}";
        dro.Y = $"{status.WPosY:0.000}";
        dro.Z = $"{status.WPosZ:0.000}";
        return dro;
    }

    /// <summary>Apply a feed/spindle override line ("M220 S80" / "M221 S120").</summary>
    public void ApplyOverride(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        if (int.TryParse(parts[0].TrimStart('M'), out var code) && int.TryParse(parts[1].TrimStart('S'), out var pct))
        {
            if (code == 220) FeedOverridePercent = Math.Clamp(pct, 10, 200);
            else if (code == 221) SpindleOverridePercent = Math.Clamp(pct, 10, 200);
        }
    }

    public string Readout => $"{State}  X {X}  Y {Y}  Z {Z}  F {Feed}  S {Spindle}";
}
