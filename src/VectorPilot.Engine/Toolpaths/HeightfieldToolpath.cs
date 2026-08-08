using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>Shared heightfield toolpath result (ported from HeightfieldToolpathResult.swift).</summary>
public sealed class HeightfieldToolpathResult
{
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PassCount { get; init; }
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds { get; init; }
}

/// <summary>Z-level roughing params (ported from HeightfieldRoughParams.swift, SPK-3D-rest).</summary>
public sealed class HeightfieldRoughParams
{
    public double ToolDiameterMm { get; set; } = 6.0;
    public double StepDownMm { get; set; } = 2.0;
    public double StepOverMm { get; set; } = 1.5;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double SafeZHeightMm { get; set; } = 5.0;
    /// <summary>Raw stock sits this far above the relief's highest point; Z=0 is stock top.</summary>
    public double StockAllowanceMm { get; set; } = 0.5;
    public double SpindleRpm { get; set; }
    /// <summary>Rest machining: &gt; 0 means this rough clears valleys narrower than the previous tool.</summary>
    public double PreviousToolDiameterMm { get; set; }

    public bool IsRestRough => PreviousToolDiameterMm > 1e-9;
}

/// <summary>Surface-following finish params (ported from HeightfieldFinishParams.swift).</summary>
public sealed class HeightfieldFinishParams
{
    public double ToolDiameterMm { get; set; } = 3.175;
    public double StepOverMm { get; set; } = 0.8;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double SpindleRpm { get; set; }
}

/// <summary>
/// Real z-level roughing from a heightfield (ported from HeightfieldRoughEngine.swift).
/// The stock is a flat block whose top sits StockAllowanceMm above the relief max;
/// each horizontal slice removes every cell whose surface is at or below that level
/// (contiguous X-runs per row), stepping down to Z=0. Z=0 is the stock top; all cut
/// depths are negative.
/// </summary>
public static class HeightfieldRoughEngine
{
    public static HeightfieldToolpathResult Compute(HeightfieldData heightfield, HeightfieldRoughParams p)
    {
        var b = heightfield.Bounds;
        double stockTop = heightfield.MaxHeight + p.StockAllowanceMm;
        double stepDown = Math.Max(0.1, p.StepDownMm);
        double stepOver = Math.Max(0.1, p.StepOverMm);

        var levels = new List<double>();
        double z = stockTop - stepDown;
        while (z > 0.001)
        {
            levels.Add(z);
            z -= stepDown;
        }
        levels.Add(0);

        var lines = new List<string> { "%", "O=ROUGH_3D" };
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        lines.Add(p.IsRestRough
            ? $"(Rest Rough: {p.ToolDiameterMm:0.0}mm after {p.PreviousToolDiameterMm:0.0}mm, {levels.Count} z-levels)"
            : $"(Rough: {p.ToolDiameterMm:0.0}mm, {levels.Count} z-levels)");

        double totalLength = 0;

        for (int pass = 0; pass < levels.Count; pass++)
        {
            double level = levels[pass];
            double depthZ = -(stockTop - level);
            lines.Add("");
            lines.Add($"(Pass {pass + 1}/{levels.Count}, Z={depthZ:0.000})");
            lines.Add($"G0 Z{p.SafeZHeightMm:0.000}");

            int rowStride = Math.Max(1, (int)Math.Round(stepOver / heightfield.CellSizeMm));
            int row = 0;
            while (row < heightfield.Height)
            {
                double cy = heightfield.MinY + (row + 0.5) * heightfield.CellSizeMm;
                int col = 0;
                while (col < heightfield.Width)
                {
                    while (col < heightfield.Width)
                    {
                        double cx = heightfield.MinX + (col + 0.5) * heightfield.CellSizeMm;
                        if (heightfield.HeightInterpolated(cx, cy) <= level + 1e-9) break;
                        col += 1;
                    }
                    if (col >= heightfield.Width) break;
                    int runStartCol = col;
                    int runEndCol = col;
                    while (runEndCol < heightfield.Width)
                    {
                        double cx = heightfield.MinX + (runEndCol + 0.5) * heightfield.CellSizeMm;
                        if (heightfield.HeightInterpolated(cx, cy) > level + 1e-9) break;
                        runEndCol += 1;
                    }

                    double runWidthMm = (runEndCol - runStartCol) * heightfield.CellSizeMm;
                    if (!p.IsRestRough || runWidthMm < p.PreviousToolDiameterMm - 1e-9)
                    {
                        double x0 = heightfield.MinX + (runStartCol + 0.5) * heightfield.CellSizeMm;
                        double x1 = heightfield.MinX + (runEndCol - 1 + 0.5) * heightfield.CellSizeMm;
                        lines.Add($"G0 X{x0:0.000} Y{cy:0.000}");
                        lines.Add($"G1 Z{depthZ:0.000} F{(int)p.PlungeFeedRateMmPerMin}");
                        lines.Add($"G1 X{x1:0.000} Y{cy:0.000} F{(int)p.FeedRateMmPerMin}");
                        totalLength += Math.Abs(x1 - x0) + p.SafeZHeightMm + stockTop - level;
                    }
                    col = runEndCol;
                }
                row += rowStride;
            }
        }

        lines.Add("");
        lines.Add("M30");
        lines.Add("%");
        return new HeightfieldToolpathResult
        {
            GcodeLines = lines,
            EstimatedTimeSeconds = totalLength / Math.Max(1, p.FeedRateMmPerMin) * 60.0,
            PassCount = levels.Count,
            Bounds = b
        };
    }
}

/// <summary>
/// Surface-following finish from a heightfield (ported from HeightfieldFinishEngine.swift).
/// Raster rows at StepOver spacing; Z follows the bilinear surface along each row.
/// </summary>
public static class HeightfieldFinishEngine
{
    public static HeightfieldToolpathResult Compute(HeightfieldData heightfield, HeightfieldFinishParams p)
    {
        var b = heightfield.Bounds;
        double stockTop = heightfield.MaxHeight;
        double stepOver = Math.Max(0.1, p.StepOverMm);

        var lines = new List<string> { "%", "O=FINISH_3D" };
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        lines.Add($"(Finish: {p.ToolDiameterMm:0.0}mm ball nose)");

        double totalLength = 0;
        int passCount = 0;
        int rowStride = Math.Max(1, (int)Math.Round(stepOver / heightfield.CellSizeMm));
        int row = 0;
        while (row < heightfield.Height)
        {
            passCount++;
            double cy = heightfield.MinY + (row + 0.5) * heightfield.CellSizeMm;
            lines.Add("");
            lines.Add($"(Pass {passCount}, Y={cy:0.000})");
            lines.Add($"G0 Z{p.SafeZHeightMm:0.000}");

            bool first = true;
            double prevX = 0;
            int col = 0;
            while (col < heightfield.Width)
            {
                double cx = heightfield.MinX + (col + 0.5) * heightfield.CellSizeMm;
                double h = heightfield.HeightInterpolated(cx, cy);
                double z = -(stockTop - h);
                if (first)
                {
                    lines.Add($"G0 X{cx:0.000} Y{cy:0.000}");
                    lines.Add($"G1 Z{z:0.000} F{(int)p.PlungeFeedRateMmPerMin}");
                    first = false;
                }
                else
                {
                    lines.Add($"G1 X{cx:0.000} Y{cy:0.000} Z{z:0.000} F{(int)p.FeedRateMmPerMin}");
                    totalLength += Math.Abs(cx - prevX);
                }
                prevX = cx;
                col += rowStride;
            }
            row += rowStride;
        }

        lines.Add("");
        lines.Add("M30");
        lines.Add("%");
        return new HeightfieldToolpathResult
        {
            GcodeLines = lines,
            EstimatedTimeSeconds = totalLength / Math.Max(1, p.FeedRateMmPerMin) * 60.0,
            PassCount = passCount,
            Bounds = b
        };
    }
}
