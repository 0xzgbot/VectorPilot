using System.Globalization;
using System.Text.RegularExpressions;

namespace VectorPilot.Engine;

/// <summary>Structured result of parsing a GRBL-style status report (ported from ShopPilot StatusParser.swift).</summary>
public sealed class ParsedMachineStatus
{
    public string State { get; set; } = "unknown";
    public double MPosX { get; set; }
    public double MPosY { get; set; }
    public double MPosZ { get; set; }
    public double WPosX { get; set; }
    public double WPosY { get; set; }
    public double WPosZ { get; set; }
    public (double Feed, double Spindle)? FS { get; set; }
    public int? Buffer { get; set; }
    public PinStatus? Pins { get; set; }
}

/// <summary>GRBL pin status from the `Pn:` field (GRBL 1.0c+).</summary>
public sealed class PinStatus
{
    public int[] Limits { get; }
    public int Probe { get; }
    public int[] Controls { get; }

    public PinStatus(int[] limits, int probe, int[] controls)
    {
        Limits = limits;
        Probe = probe;
        Controls = controls;
    }
}

/// <summary>Parses GRBL v1.x status reports. Format: `&lt;State|MPos:x,y,z|WPos:x,y,z|FS:r,s|Bf:n|Pn:xxx|x|xxx&gt;`</summary>
public static class StatusParser
{
    private static readonly string[] ValidStates = { "Idle", "Run", "Hold", "Home", "Alarm", "Check", "Door" };
    private static readonly Regex TrailingUnitsRegex = new("[a-zA-Z]", RegexOptions.Compiled);

    public static ParsedMachineStatus? Parse(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('<') || !text.EndsWith('>')) return null;

        var inner = text[1..^1];
        var components = inner.Split('|');
        var status = new ParsedMachineStatus();

        int index = 0;
        while (index < components.Length)
        {
            var component = components[index];
            if (component.StartsWith("Pn:", StringComparison.Ordinal))
            {
                var group = component;
                int cursor = index + 1;
                while (cursor < components.Length &&
                       !components[cursor].Contains(':') &&
                       group.Split('|').Length < 3)
                {
                    group += "|" + components[cursor];
                    cursor++;
                }
                status.Pins = ParsePins(group);
                index = cursor;
                continue;
            }

            var state = ParseState(component);
            if (state != null)
            {
                status.State = state;
            }
            else if (TryParseCoordinates(component, "MPos:", out var m))
            {
                status.MPosX = m.X; status.MPosY = m.Y; status.MPosZ = m.Z;
            }
            else if (TryParseCoordinates(component, "WPos:", out var w))
            {
                status.WPosX = w.X; status.WPosY = w.Y; status.WPosZ = w.Z;
            }
            else if (TryParseFS(component, out var fs))
            {
                status.FS = fs;
            }
            else if (TryParseBuffer(component, out var buf))
            {
                status.Buffer = buf;
            }
            index++;
        }

        return status;
    }

    private static string? ParseState(string component)
    {
        foreach (var s in ValidStates)
            if (component == s) return s;
        return null;
    }

    private static bool TryParseCoordinates(string component, string prefix, out (double X, double Y, double Z) coords)
    {
        coords = default;
        if (!component.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var values = component[prefix.Length..];
        var parts = values.Split(',');
        if (parts.Length < 3) return false;

        static double ToDouble(string s)
        {
            var cleaned = TrailingUnitsRegex.Replace(s.Trim(), "");
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }

        var x = ToDouble(parts[0]);
        var y = ToDouble(parts[1]);
        var z = ToDouble(parts[2]);
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return false;
        coords = (x, y, z);
        return true;
    }

    private static bool TryParseFS(string component, out (double Feed, double Spindle) fs)
    {
        fs = default;
        if (!component.StartsWith("FS:", StringComparison.Ordinal)) return false;
        var parts = component[3..].Split(',');
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var feed)) return false;
        var spindle = parts.Length > 1
            ? (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0.0)
            : 0.0;
        fs = (feed, spindle);
        return true;
    }

    private static bool TryParseBuffer(string component, out int buffer)
    {
        buffer = 0;
        if (!component.StartsWith("Bf:", StringComparison.Ordinal)) return false;
        // GRBL 1.1 reports "Bf:planner,rx" — take the planner count.
        var value = component[3..].Trim();
        var first = value.Split(',')[0].Trim();
        return int.TryParse(first, out buffer);
    }

    private static PinStatus? ParsePins(string component)
    {
        if (!component.StartsWith("Pn:", StringComparison.Ordinal)) return null;
        var parts = component[3..].Split('|');
        if (parts.Length < 3) return null;

        static int[] ParseBinaryGroup(string s)
        {
            var list = new List<int>();
            foreach (var ch in s.Trim())
                list.Add(ch == '1' ? 1 : 0);
            return list.ToArray();
        }

        var limits = ParseBinaryGroup(parts[0]);
        var probe = parts[1].Trim() == "1" ? 1 : 0;
        var controls = ParseBinaryGroup(parts[2]);
        return new PinStatus(limits, probe, controls);
    }
}
