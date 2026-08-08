using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Photo V-Carve params (ported from PhotoVCarveToolpathParams.swift, SPK-0901).</summary>
public sealed class PhotoVCarveToolpathParams
{
    public double VBitAngleDegrees { get; set; } = 60.0;
    public double MaxDepthMm { get; set; } = 3.0;
    public double StepOverMm { get; set; } = 0.5;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1200;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

/// <summary>
/// Photo V-Carve (ported from PhotoVCarveToolpathEngine.swift): a V-bit raster
/// where pixel brightness maps to cut depth — dark carves deep, bright stays
/// high. depth = (1 − luminance) × maxDepth.
/// </summary>
public static class PhotoVCarveEngine
{
    public static SpecialtyResult Compute(HeightfieldData heightfield, PhotoVCarveToolpathParams p)
    {
        double maxH = Math.Max(heightfield.MaxHeight, 1e-9);
        double stepOver = Math.Max(0.1, p.StepOverMm);
        double stockTop = heightfield.MaxHeight;

        var lines = new List<string> { "%", "O=PHOTO_V_CARVE_TOOLPATH" };
        lines.Add($"(Photo V-Carve: V-bit {(int)p.VBitAngleDegrees}° · depth {p.MaxDepthMm:0.00}mm · step {p.StepOverMm:0.00}mm)");
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        double totalLength = 0;
        int passCount = 0;

        int rowStride = Math.Max(1, (int)Math.Round(stepOver / heightfield.CellSizeMm));
        int row = 0;
        while (row < heightfield.Height)
        {
            passCount++;
            double cy = heightfield.MinY + (row + 0.5) * heightfield.CellSizeMm;
            lines.Add("");
            lines.Add($"(Photo pass {passCount}, Y={cy:0.000})");
            lines.Add($"G0 Z{p.SafeZHeightMm:0.000}");

            bool first = true;
            double prevX = 0;
            int col = 0;
            while (col < heightfield.Width)
            {
                double cx = heightfield.MinX + (col + 0.5) * heightfield.CellSizeMm;
                double h = heightfield.HeightInterpolated(cx, cy);
                double luminance = Math.Clamp(h / maxH, 0.0, 1.0);
                double depth = (1.0 - luminance) * p.MaxDepthMm;
                double z = -(stockTop - h) - depth;
                if (first)
                {
                    lines.Add($"G0 X{cx:0.000} Y{cy:0.000}");
                    lines.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
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
        double time = totalLength / Math.Max(1, p.FeedRateMmPerMin) * 60.0;
        return new SpecialtyResult { GcodeLines = lines, EstimatedTimeSeconds = time, FeatureCount = passCount };
    }
}
