using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>
/// Drill-point style for a Drill Bank (ported from DrillBankToolpath.swift): "through"
/// reaches the full cut depth; "brad-point" stops short (0.8× depth) so the center
/// point of a brad-point bit seats before the flutes engage.
/// </summary>
public enum DrillBankPointStyle
{
    Through,
    BradPoint
}

public static class DrillBankPointStyleExtensions
{
    public static string DisplayName(this DrillBankPointStyle s) => s switch
    {
        DrillBankPointStyle.Through => "Through",
        _ => "Brad-point"
    };
}

/// <summary>
/// A rectangular grid of drill holes (columns × rows at a spacing) with unique
/// per-hole numbers (ported from DrillBankToolpathParams.swift).
/// </summary>
public sealed class DrillBankParams
{
    public int GridCols { get; set; } = 3;
    public int GridRows { get; set; } = 2;
    public double SpacingX { get; set; } = 20.0;
    public double SpacingY { get; set; } = 25.0;
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double ToolDiameterMm { get; set; } = 6.0;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double SafetyHeightMm { get; set; } = 10.0;
    public double CutDepthMm { get; set; } = 10.0;
    public DrillBankPointStyle Style { get; set; } = DrillBankPointStyle.Through;

    /// <summary>Linked spindle RPM (0 = not configured; engine emits M3 S only when &gt; 0).</summary>
    public double SpindleRpm { get; set; }

    /// <summary>
    /// Generate the grid of drill positions. Row-major: column index changes fastest
    /// (col 0..cols-1 at row 0, then row 1, …). At least one row and one column.
    /// </summary>
    public List<DrillPoint> GridPoints()
    {
        var points = new List<DrillPoint>();
        int rows = Math.Max(GridRows, 1);
        int cols = Math.Max(GridCols, 1);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                points.Add(new DrillPoint(
                    OriginX + col * SpacingX,
                    OriginY + row * SpacingY,
                    -CutDepthMm));
            }
        }
        return points;
    }
}

/// <summary>Computed drill-bank toolpath result (ported from DrillBankToolpathResult.swift).</summary>
public sealed class DrillBankResult
{
    public DrillBankParams Params { get; init; } = new();
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PointCount { get; init; }
}

/// <summary>
/// Computes a drill-bank toolpath: a W×H grid of uniquely-numbered holes. Each hole:
/// rapid to position, rapid to safety height, plunge to depth (through = full cutDepth;
/// brad-point = 0.8×cutDepth), retract to safety. Ported from DrillBankToolpathEngine.swift.
/// </summary>
public static class DrillBankEngine
{
    /// <summary>
    /// Compute the drill-bank G-code. When <paramref name="points"/> is non-empty it overrides
    /// the generated grid (callers may feed a custom point list).
    /// </summary>
    public static DrillBankResult Compute(IReadOnlyList<DrillPoint>? points, DrillBankParams p, double stockHeightMm = 25.0)
    {
        var drillPoints = points is { Count: > 0 } ? points : p.GridPoints();
        double plungeFeed = p.PlungeFeedRateMmPerMin;
        double plungeDepth = p.CutDepthMm;

        var all = new List<string>
        {
            "%",
            "O=DRILL_BANK_TOOLPATH",
            $"(Drill Bank: {p.GridCols}x{p.GridRows} grid — {drillPoints.Count} holes)",
            $"(Tool: {(int)(p.ToolDiameterMm * 10)}mm)",
            $"(Style: {p.Style.DisplayName()})"
        };
        if (p.SpindleRpm > 0)
        {
            all.Add($"M3 S{(int)p.SpindleRpm}");
        }

        for (int index = 0; index < drillPoints.Count; index++)
        {
            var point = drillPoints[index];
            int holeNumber = index + 1;
            all.Add("");
            all.Add($"(Hole {holeNumber}/{drillPoints.Count}: X{F3(point.X)} Y{F3(point.Y)})");
            all.Add($"G0 X{F3(point.X)} Y{F3(point.Y)}");
            all.Add($"G0 Z{F1(p.SafetyHeightMm)}");

            double targetDepth;
            switch (p.Style)
            {
                case DrillBankPointStyle.Through:
                    targetDepth = -plungeDepth;
                    break;
                default: // BradPoint
                    targetDepth = -plungeDepth * 0.8;
                    all.Add($"(Brad-point: seats the center point at {F1(plungeDepth * 0.8)}mm — full depth {F1(plungeDepth)}mm)");
                    break;
            }
            all.Add($"G1 Z{F3(targetDepth)} F{(int)plungeFeed}");
            all.Add($"G0 Z{F1(p.SafetyHeightMm)}");
        }

        all.Add("");
        all.Add("M30");
        all.Add("%");

        double totalDrillDepth = drillPoints.Count * plungeDepth;
        double estimatedTimeSeconds = totalDrillDepth / Math.Max(plungeFeed, 1e-9) * 60.0 + drillPoints.Count * 2.0;

        return new DrillBankResult
        {
            Params = p,
            GcodeLines = all,
            EstimatedTimeSeconds = estimatedTimeSeconds,
            PointCount = drillPoints.Count
        };
    }

    private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
