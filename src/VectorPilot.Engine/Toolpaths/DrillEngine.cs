using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>Types of drilling operations supported by ShopPilot (ported from DrillToolpath.swift).</summary>
public enum DrillCycleType
{
    /// <summary>Simple peck drill with retract.</summary>
    PeckDrill,
    /// <summary>Deep hole drilling with multiple pecks and full retract at bottom.</summary>
    DeepHolePeck,
    /// <summary>Spot drilling for center punch location.</summary>
    SpotDrill,
    /// <summary>Counterboring for flat-bottomed holes.</summary>
    Counterbore,
    /// <summary>Countersinking for conical holes.</summary>
    Countersink
}

public static class DrillCycleTypeExtensions
{
    public static string DisplayName(this DrillCycleType t) => t switch
    {
        DrillCycleType.PeckDrill => "Peck Drill",
        DrillCycleType.DeepHolePeck => "Deep Hole Peck",
        DrillCycleType.SpotDrill => "Spot Drill",
        DrillCycleType.Counterbore => "Counterbore",
        _ => "Countersink"
    };
}

/// <summary>Retract strategy for peck cycles (ported from DrillToolpath.swift).</summary>
public enum DrillRetractMode
{
    AboveCuttingStart,
    AbovePreviousPass
}

public static class DrillRetractModeExtensions
{
    public static string DisplayName(this DrillRetractMode m) => m switch
    {
        DrillRetractMode.AboveCuttingStart => "Above Cutting Start",
        _ => "Above Previous Pass Height"
    };
}

/// <summary>A single drill point with position and depth. X/Y in mm, Z depth negative.</summary>
public readonly record struct DrillPoint(double X, double Y, double ZDepthMm,
    double DwellSeconds = 0.0, double OverrideFeedRate = 0.0);

/// <summary>Configuration for a drill toolpath operation (ported from DrillToolpathParams.swift).</summary>
public sealed class DrillParams
{
    public DrillCycleType CycleType { get; set; } = DrillCycleType.PeckDrill;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double RetractHeightMm { get; set; } = 5.0;
    public double PeckDepthMm { get; set; } = 2.0;
    public double ToolDiameterMm { get; set; } = 6.0;

    /// <summary>Safety height above workpiece.</summary>
    public double SafetyHeightMm { get; set; } = 10.0;

    /// <summary>Linked spindle RPM (0 = not configured; engines emit M3 S only when &gt; 0).</summary>
    public double SpindleRpm { get; set; }

    public double StartDepthMm { get; set; }
    public double CutDepthMm { get; set; } = 10.0;
    public bool PeckDrilling { get; set; } = true;
    public DrillRetractMode RetractMode { get; set; } = DrillRetractMode.AboveCuttingStart;
    public double PeckRetractGapMm { get; set; } = 2.0;
    public bool DwellAtBottom { get; set; }
    public double DwellTimeSeconds { get; set; } = 0.25;
    public bool UseVectorSelectionOrder { get; set; }
}

/// <summary>Computed drill toolpath result (ported from DrillToolpathResult.swift).</summary>
public sealed class DrillResult
{
    public DrillParams Params { get; init; } = new();
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PointCount { get; init; }

    /// <summary>Total drilling depth across all points (placeholder in the Swift source).</summary>
    public double TotalDrillDepthMm => 0.0;
}

/// <summary>
/// Computes drill toolpaths from a set of drill points with configurable cycle types
/// (ported from DrillToolpathEngine.swift). Units: mm; G0 rapids, G1 plunges at the
/// plunge feed rate, G4 dwells. Line-for-line faithful to the Swift emission.
/// </summary>
public static class DrillEngine
{
    public static DrillResult Compute(IReadOnlyList<DrillPoint> points, DrillParams p, double stockHeightMm = 25.0)
    {
        double plungeFeed = p.PlungeFeedRateMmPerMin;

        // G-code header
        var all = new List<string>
        {
            "%",
            "O=DRILL_TOOLPATH",
            $"(Tool: {(int)(p.ToolDiameterMm * 10)}mm)",
            $"(Cycle: {p.CycleType.DisplayName()})"
        };
        if (p.SpindleRpm > 0)
        {
            all.Add($"M3 S{(int)p.SpindleRpm}");
        }

        double totalDrillDepth = 0.0;

        for (int index = 0; index < points.Count; index++)
        {
            var point = points[index];

            // Rapid to safe height
            all.Add("");
            all.Add($"(Point {index + 1}/{points.Count}: X{F3(point.X)} Y{F3(point.Y)})");
            all.Add($"G0 X{F3(point.X)} Y{F3(point.Y)}");
            all.Add($"G0 Z{F1(p.SafetyHeightMm)}");

            switch (p.CycleType)
            {
                case DrillCycleType.PeckDrill:
                    all.AddRange(GeneratePeckDrill(point, p, plungeFeed));
                    break;
                case DrillCycleType.DeepHolePeck:
                    all.AddRange(GenerateDeepHolePeck(point, p, plungeFeed));
                    break;
                case DrillCycleType.SpotDrill:
                    all.AddRange(GenerateSpotDrill(point, p, plungeFeed));
                    break;
                case DrillCycleType.Counterbore:
                    all.AddRange(GenerateCounterbore(point, p, plungeFeed));
                    break;
                default:
                    all.AddRange(GenerateCountersink(point, p, plungeFeed));
                    break;
            }

            // Rapid to safe height after each hole
            all.Add($"G0 Z{F1(p.SafetyHeightMm)}");

            totalDrillDepth += Math.Abs(point.ZDepthMm);
        }

        // G-code footer
        all.Add("");
        all.Add("M30");
        all.Add("%");

        return new DrillResult
        {
            Params = p,
            GcodeLines = all,
            EstimatedTimeSeconds = totalDrillDepth / Math.Max(plungeFeed, 1e-9) * 60.0 + points.Count * 2.0,
            PointCount = points.Count
        };
    }

    /// <summary>Peck drill cycle: plunge peckDepth per pass, rapid retract to retractHeight between pecks.</summary>
    private static List<string> GeneratePeckDrill(DrillPoint point, DrillParams p, double plungeFeed)
    {
        var g = new List<string>();
        double peckDepth = p.PeckDepthMm;
        double totalDepth = Math.Abs(point.ZDepthMm);
        double retractHeight = p.RetractHeightMm;

        // Zero/negative peck depth would divide by zero — fall back to a single exact plunge.
        if (peckDepth <= 0)
        {
            g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");
            if (point.DwellSeconds > 0)
            {
                g.Add($"G4 P{Fsw(point.DwellSeconds)}");
            }
            return g;
        }

        int numPecks = (int)Math.Ceiling(totalDepth / peckDepth);

        for (int peck = 1; peck <= numPecks; peck++)
        {
            double currentDepth = -peck * peckDepth;

            if (Math.Abs(currentDepth) >= totalDepth)
            {
                // Final pass to full depth
                g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");
                if (point.DwellSeconds > 0)
                {
                    g.Add($"G4 P{Fsw(point.DwellSeconds)}");
                }
            }
            else
            {
                // Peck to this depth and retract
                g.Add($"G1 Z{F3(currentDepth)} F{(int)plungeFeed}");
                g.Add($"G0 Z{F1(retractHeight)}");
            }
        }

        return g;
    }

    /// <summary>Deep hole peck cycle: same as peck but fully retracts to safety height.</summary>
    private static List<string> GenerateDeepHolePeck(DrillPoint point, DrillParams p, double plungeFeed)
    {
        var g = new List<string>();
        double peckDepth = p.PeckDepthMm;
        double totalDepth = Math.Abs(point.ZDepthMm);

        if (peckDepth <= 0)
        {
            g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");
            if (point.DwellSeconds > 0)
            {
                g.Add($"G4 P{Fsw(point.DwellSeconds)}");
            }
            return g;
        }

        int numPecks = (int)Math.Ceiling(totalDepth / peckDepth);

        for (int peck = 1; peck <= numPecks; peck++)
        {
            double currentDepth = -peck * peckDepth;

            if (Math.Abs(currentDepth) >= totalDepth)
            {
                // Final pass to full depth with dwell
                g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");
                if (point.DwellSeconds > 0)
                {
                    g.Add($"G4 P{Fsw(point.DwellSeconds)}");
                }
            }
            else
            {
                // Peck to this depth and fully retract
                g.Add($"G1 Z{F3(currentDepth)} F{(int)plungeFeed}");
                g.Add($"G0 Z{F1(p.SafetyHeightMm)}");
            }
        }

        return g;
    }

    /// <summary>Spot drill: goes shallow (15% of full depth) with a brief dwell.</summary>
    private static List<string> GenerateSpotDrill(DrillPoint point, DrillParams p, double plungeFeed)
    {
        var g = new List<string>();

        // Spot drill only needs to go shallow (typically 10-20% of full depth)
        double spotDepth = point.ZDepthMm * 0.15;

        g.Add($"G1 Z{F3(spotDepth)} F{(int)plungeFeed}");

        // Brief dwell to create center punch
        if (point.DwellSeconds > 0)
        {
            g.Add($"G4 P{Fsw(point.DwellSeconds)}");
        }
        else
        {
            g.Add("G4 P0.5"); // Default 0.5s dwell for spot drilling
        }

        return g;
    }

    /// <summary>Counterbore: full depth with 1s dwell for a flat bottom.</summary>
    private static List<string> GenerateCounterbore(DrillPoint point, DrillParams p, double plungeFeed)
    {
        var g = new List<string>();

        g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");

        if (point.DwellSeconds > 0)
        {
            g.Add($"G4 P{Fsw(point.DwellSeconds)}");
        }
        else
        {
            g.Add("G4 P1.0"); // Default 1s dwell for counterboring
        }

        return g;
    }

    /// <summary>Countersink: full depth with 0.5s dwell.</summary>
    private static List<string> GenerateCountersink(DrillPoint point, DrillParams p, double plungeFeed)
    {
        var g = new List<string>();

        g.Add($"G1 Z{F3(point.ZDepthMm)} F{(int)plungeFeed}");

        if (point.DwellSeconds > 0)
        {
            g.Add($"G4 P{Fsw(point.DwellSeconds)}");
        }
        else
        {
            g.Add("G4 P0.5"); // Default 0.5s dwell for countersinking
        }

        return g;
    }

    private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    // Swift Double interpolation prints the shortest round-tripping representation
    // (e.g. 0.25 -> "0.25", 0.5 -> "0.5"); "0.0#####..." reproduces that for G4 P.
    private static string Fsw(double v) => v.ToString("0.0################", CultureInfo.InvariantCulture);
}
