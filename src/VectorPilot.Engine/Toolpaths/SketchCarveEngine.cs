namespace VectorPilot.Engine;

/// <summary>Sketch carving params (ported from SketchCarveToolpathParams.swift).</summary>
public sealed class SketchCarveToolpathParams
{
    public double VBitAngleDegrees { get; set; } = 60.0;
    public double MaxDepthMm { get; set; } = 2.5;
    public double EdgeThreshold { get; set; } = 0.12; // 0…1 normalized gradient gate
    public double StepOverMm { get; set; } = 0.5;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1200;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

/// <summary>
/// Sketch carving (ported from SketchCarveToolpathEngine.swift): carves only
/// the EDGES of the image — a Sobel gradient map gates the V-bit; strong
/// brightness transitions carve deep V-lines, flat areas stay untouched.
/// </summary>
public static class SketchCarveEngine
{
    public static SpecialtyResult Compute(HeightfieldData heightfield, SketchCarveToolpathParams p)
    {
        int w = heightfield.Width, h = heightfield.Height;
        double stepOver = Math.Max(0.1, p.StepOverMm);

        // 1. Sobel gradient magnitude per cell (clamped borders).
        var mag = new double[w * h];
        double maxMag = 0;
        double At(int r, int c)
        {
            int rr = Math.Clamp(r, 0, h - 1);
            int cc = Math.Clamp(c, 0, w - 1);
            return heightfield.Heights[rr * w + cc];
        }
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double gx = -At(r - 1, c - 1) - 2 * At(r, c - 1) - At(r + 1, c - 1)
                            + At(r - 1, c + 1) + 2 * At(r, c + 1) + At(r + 1, c + 1);
                double gy = -At(r - 1, c - 1) - 2 * At(r - 1, c) - At(r - 1, c + 1)
                            + At(r + 1, c - 1) + 2 * At(r + 1, c) + At(r + 1, c + 1);
                double m = Math.Sqrt(gx * gx + gy * gy);
                mag[r * w + c] = m;
                if (m > maxMag) maxMag = m;
            }
        }

        // 2. Normalize + threshold into the edge map.
        double norm = Math.Max(maxMag, 1e-9);
        double threshold = Math.Clamp(p.EdgeThreshold, 0, 1);
        var edge = mag.Select(m =>
        {
            double e = m / norm;
            return e >= threshold ? e : 0.0;
        }).ToArray();

        // 3. Raster the edge map (mirrors photo V-Carve's row walk).
        var lines = new List<string> { "%", "O=SKETCH_CARVE_TOOLPATH" };
        lines.Add($"(Sketch Carve: V-bit {(int)p.VBitAngleDegrees}° · depth {p.MaxDepthMm:0.00}mm · edge ≥ {threshold * 100:0}%)");
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        double totalLength = 0;
        int passCount = 0;
        int carvedCells = 0;

        int rowStride = Math.Max(1, (int)Math.Round(stepOver / heightfield.CellSizeMm));
        int row = 0;
        while (row < h)
        {
            passCount++;
            double cy = heightfield.MinY + (row + 0.5) * heightfield.CellSizeMm;
            lines.Add("");
            lines.Add($"(Sketch pass {passCount}, Y={cy:0.000})");
            lines.Add($"G0 Z{p.SafeZHeightMm:0.000}");

            bool first = true;
            double prevX = 0;
            int col = 0;
            while (col < w)
            {
                double cx = heightfield.MinX + (col + 0.5) * heightfield.CellSizeMm;
                double depth = edge[row * w + col] * p.MaxDepthMm;
                double z = -depth;
                if (depth > 1e-6) carvedCells++;
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
        return new SpecialtyResult { GcodeLines = lines, EstimatedTimeSeconds = time, FeatureCount = carvedCells };
    }
}
