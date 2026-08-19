namespace VectorPilot.Engine;

/// <summary>One toolpath's contribution to the job estimate.</summary>
public sealed class ToolpathTimeBreakdown
{
    public required string Name { get; init; }
    public Guid ToolId { get; init; }
    public double CuttingSeconds { get; init; }
    public double RapidSeconds { get; init; }
    public double TotalSeconds => CuttingSeconds + RapidSeconds;
}

/// <summary>Whole-job time estimate with a cut/travel split and tool changes.</summary>
public sealed class JobTimeEstimate
{
    public List<ToolpathTimeBreakdown> Toolpaths { get; init; } = new();
    public double CuttingSeconds { get; init; }
    public double RapidSeconds { get; init; }
    public double ToolChangeSeconds { get; init; }
    public int ToolChanges { get; init; }

    public double TotalSeconds => CuttingSeconds + RapidSeconds + ToolChangeSeconds;
    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);

    /// <summary>"1h 24m" / "8m 30s" / "45s".</summary>
    public string Formatted
    {
        get
        {
            var t = Total;
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }
    }
}

/// <summary>
/// Aggregates per-toolpath estimates into a whole-job figure (card E4). Splits
/// cutting from rapid travel by walking the posted G-code, and adds a fixed cost
/// per tool change when consecutive toolpaths use different tools.
/// </summary>
public static class JobTimeEstimator
{
    /// <summary>Default manual tool-change allowance, in seconds.</summary>
    public const double DefaultToolChangeSeconds = 45.0;

    public static JobTimeEstimate Estimate(
        IReadOnlyList<Toolpath> toolpaths,
        double rapidFeedMmPerMin = 5000,
        double toolChangeSeconds = DefaultToolChangeSeconds)
    {
        var rows = new List<ToolpathTimeBreakdown>();
        double cutting = 0, rapid = 0;
        int changes = 0;
        Guid? lastTool = null;

        foreach (var tp in toolpaths)
        {
            var (cutSec, rapidSec) = Split(tp, rapidFeedMmPerMin);
            cutting += cutSec;
            rapid += rapidSec;

            if (lastTool is not null && tp.ToolId != lastTool) changes++;
            lastTool = tp.ToolId;

            rows.Add(new ToolpathTimeBreakdown
            {
                Name = tp.Name,
                ToolId = tp.ToolId,
                CuttingSeconds = cutSec,
                RapidSeconds = rapidSec
            });
        }

        return new JobTimeEstimate
        {
            Toolpaths = rows,
            CuttingSeconds = cutting,
            RapidSeconds = rapid,
            ToolChanges = changes,
            ToolChangeSeconds = changes * toolChangeSeconds
        };
    }

    /// <summary>
    /// Cut vs rapid seconds for one toolpath. Prefers walking the posted G-code;
    /// falls back to the engine's own EstimatedTimeSeconds when no lines exist.
    /// </summary>
    private static (double Cutting, double Rapid) Split(Toolpath tp, double rapidFeed)
    {
        var lines = tp.GCode;
        if (lines.Count == 0)
            return (tp.EstimatedTimeSeconds, 0);

        double cutting = 0, rapid = 0;
        double x = 0, y = 0, z = 0, feed = 0;
        bool have = false;

        foreach (var raw in lines)
        {
            var line = raw.Split(';')[0].Split('(')[0].Trim();
            if (line.Length == 0) continue;

            bool isRapid = line.StartsWith("G0", StringComparison.OrdinalIgnoreCase);
            bool isFeed = line.StartsWith("G1", StringComparison.OrdinalIgnoreCase);
            if (!isRapid && !isFeed) continue;

            double nx = x, ny = y, nz = z;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v)) continue;
                switch (char.ToUpperInvariant(tok[0]))
                {
                    case 'X': nx = v; break;
                    case 'Y': ny = v; break;
                    case 'Z': nz = v; break;
                    case 'F': feed = v; break;
                }
            }

            if (have)
            {
                double dist = Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y) + (nz - z) * (nz - z));
                if (dist > 1e-9)
                {
                    double rate = isRapid ? rapidFeed : (feed > 1e-9 ? feed : rapidFeed);
                    double sec = dist / rate * 60.0;
                    if (isRapid) rapid += sec; else cutting += sec;
                }
            }

            x = nx; y = ny; z = nz;
            have = true;
        }

        // No motion parsed (comments/setup only): fall back to the engine figure.
        if (cutting + rapid < 1e-9) return (tp.EstimatedTimeSeconds, 0);
        return (cutting, rapid);
    }
}
