using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Tab generator (Aspire tabs parity): post-processes a profile/pocket G-code
/// pass list, lifting the tool to SafeZ across tab spans so the part stays
/// tethered. Tabs are placed at fixed spacing along the cut path.
/// </summary>
public static class TabGenerator
{
    /// <summary>
    /// Insert tab lifts into the motion lines of one pass. `points` is the cut
    /// path (tool-center polyline); `tabLengthMm` is the lifted span, spacing
    /// the nominal center-to-center distance. Returns the motion lines with
    /// G0 lifts inserted (SafeZ + return) at each tab.
    /// </summary>
    public static List<string> AddTabs(
        IReadOnlyList<VectorPoint> points,
        IReadOnlyList<string> motionLines,
        double tabLengthMm,
        double tabSpacingMm,
        double safeZ)
    {
        if (tabLengthMm <= 1e-9 || tabSpacingMm <= 1e-9 || points.Count < 2)
        {
            return motionLines.ToList();
        }

        // Cumulative path length.
        var cum = new List<double> { 0 };
        for (int i = 1; i < points.Count; i++)
        {
            cum.Add(cum[^1] + points[i - 1].DistanceTo(points[i]));
        }
        double total = cum[^1];
        if (total <= 1e-9) return motionLines.ToList();

        // Tab center positions along the path.
        var tabCenters = new List<double>();
        double pos = tabSpacingMm / 2;
        while (pos < total)
        {
            tabCenters.Add(pos);
            pos += tabSpacingMm;
        }
        if (tabCenters.Count == 0) return motionLines.ToList();

        // Map each tab center to a line index + lift the span covering it.
        var liftStartLine = new HashSet<int>();
        var liftEndLine = new HashSet<int>();
        foreach (double center in tabCenters)
        {
            double start = center - tabLengthMm / 2;
            double end = center + tabLengthMm / 2;
            liftStartLine.Add(LineIndexAt(cum, Math.Max(0, start)));
            liftEndLine.Add(LineIndexAt(cum, Math.Min(total, end)));
        }

        var result = new List<string>();
        for (int i = 0; i < motionLines.Count; i++)
        {
            if (liftStartLine.Contains(i))
            {
                result.Add($"G0 Z{safeZ:0.000} ; tab");
            }
            result.Add(motionLines[i]);
            if (liftEndLine.Contains(i) && i < motionLines.Count - 1)
            {
                result.Add($"G1 Z{PlungeZ(motionLines[i]):0.000} ; tab end");
            }
        }
        return result;
    }

    private static int LineIndexAt(List<double> cum, double length)
    {
        for (int i = 0; i < cum.Count - 1; i++)
        {
            if (length <= cum[i + 1] + 1e-9) return i;
        }
        return cum.Count - 2;
    }

    private static double PlungeZ(string line)
    {
        // Reuse the last Z on the line (plunge lines carry the pass depth).
        int idx = line.LastIndexOf("Z", StringComparison.Ordinal);
        if (idx < 0) return -1.0;
        var rest = line[(idx + 1)..].Split(' ')[0];
        return double.TryParse(rest, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z) ? z : -1.0;
    }
}

/// <summary>
/// Ramp generator (Aspire ramping parity): replaces the vertical plunge into
/// the first cut of a pass with a linear (smooth), zigzag, or spiral ramp
/// over `rampDistanceMm` along the path.
/// </summary>
public static class RampGenerator
{
    public enum RampType { None, Smooth, ZigZag, Spiral }

    /// <summary>Convert the plunge + first cut move of a pass into a ramp entry.</summary>
    public static List<string> BuildRamp(RampType type, VectorPoint start, VectorPoint next, double fromZ, double toZ, double rampDistanceMm, double feed, double plungeFeed)
    {
        if (type == RampType.None || rampDistanceMm <= 1e-9)
        {
            return new List<string> { $"G1 Z{toZ:0.000} F{(int)plungeFeed}" };
        }

        double dx = next.X - start.X, dy = next.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 1e-9) return new List<string> { $"G1 Z{toZ:0.000} F{(int)plungeFeed}" };
        double ux = dx / len, uy = dy / len;
        double rampLen = Math.Min(rampDistanceMm, len);
        int steps = Math.Max(2, (int)(rampLen / 0.5));

        var lines = new List<string>();
        switch (type)
        {
            case RampType.Smooth:
                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double x = start.X + ux * rampLen * t;
                    double y = start.Y + uy * rampLen * t;
                    double z = fromZ + (toZ - fromZ) * t;
                    lines.Add($"G1 X{x:0.000} Y{y:0.000} Z{z:0.000} F{(int)feed}");
                }
                break;
            case RampType.ZigZag:
            {
                // Zigzag along the path: alternate X overshoot around the line.
                double half = Math.Min(0.5, rampLen * 0.1);
                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double side = (i % 2 == 0 ? 1 : -1) * half * (1 - t);
                    double x = start.X + ux * rampLen * t - uy * side;
                    double y = start.Y + uy * rampLen * t + ux * side;
                    double z = fromZ + (toZ - fromZ) * t;
                    lines.Add($"G1 X{x:0.000} Y{y:0.000} Z{z:0.000} F{(int)feed}");
                }
                break;
            }
            case RampType.Spiral:
            {
                // Corkscrew around the entry point while descending.
                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double a = t * 2 * Math.PI * 2;
                    double r = rampLen * 0.25 * (1 - t);
                    double x = start.X + Math.Cos(a) * r;
                    double y = start.Y + Math.Sin(a) * r;
                    double z = fromZ + (toZ - fromZ) * t;
                    lines.Add($"G1 X{x:0.000} Y{y:0.000} Z{z:0.000} F{(int)feed}");
                }
                break;
            }
        }
        return lines;
    }
}
