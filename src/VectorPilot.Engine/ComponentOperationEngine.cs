namespace VectorPilot.Engine;

public enum EmbossType { Raised, Recessed, Stroke, Letterpress }

/// <summary>Smooth params (ported from SmoothParams).</summary>
public sealed class SmoothParams
{
    public int Iterations { get; set; } = 1;
    public double SmoothingFactor { get; set; } = 0.5;
    public bool PreserveVolume { get; set; }
}

/// <summary>Emboss params (ported from EmbossParams).</summary>
public sealed class EmbossParams
{
    public EmbossType EmbossType { get; set; } = EmbossType.Raised;
    public double Depth { get; set; } = 1.0;
}

/// <summary>
/// Component-level 3D operations (ported from ComponentOperationEngine.swift, SPK-0712):
/// smooth (Laplacian), emboss (rounded stamp), bake (composite the visible stack),
/// split (cut at a horizontal plane, keep the above part re-based to 0).
/// </summary>
public static class ComponentOperationEngine
{
    /// <summary>Laplacian smooth: each cell moves toward the mean of its 4-neighbours
    /// by `factor` per iteration. Grid geometry preserved.</summary>
    public static HeightfieldData Smooth(HeightfieldData hf, SmoothParams p)
    {
        var heights = (double[])hf.Heights.Clone();
        int w = hf.Width, h = hf.Height;
        for (int _ = 0; _ < Math.Max(1, p.Iterations); _++)
        {
            var next = (double[])heights.Clone();
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    double sum = 0;
                    int count = 0;
                    if (i > 0) { sum += heights[j * w + (i - 1)]; count++; }
                    if (i < w - 1) { sum += heights[j * w + (i + 1)]; count++; }
                    if (j > 0) { sum += heights[(j - 1) * w + i]; count++; }
                    if (j < h - 1) { sum += heights[(j + 1) * w + i]; count++; }
                    if (count == 0) continue;
                    double mean = sum / count;
                    next[j * w + i] = heights[j * w + i] + (mean - heights[j * w + i]) * p.SmoothingFactor;
                }
            }
            heights = next;
        }
        if (p.PreserveVolume)
        {
            double originalMean = hf.Heights.Average();
            double newMean = heights.Average();
            double shift = originalMean - newMean;
            for (int i = 0; i < heights.Length; i++) heights[i] = Math.Max(0, heights[i] + shift);
        }
        return New(hf, heights);
    }

    /// <summary>Emboss a rounded stamp (dome, peak = depth at center) into the relief.</summary>
    public static HeightfieldData Emboss(HeightfieldData hf, EmbossParams p)
    {
        int w = hf.Width, h = hf.Height;
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        double maxR = Math.Sqrt(cx * cx + cy * cy);
        var heights = (double[])hf.Heights.Clone();
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                double r = Math.Sqrt(Math.Pow(i - cx, 2) + Math.Pow(j - cy, 2)) / Math.Max(maxR, 1e-9);
                double stamp = p.Depth * (1.0 - Math.Min(1.0, r));
                int idx = j * w + i;
                heights[idx] = p.EmbossType == EmbossType.Raised
                    ? hf.Heights[idx] + stamp
                    : Math.Max(0, hf.Heights[idx] - stamp); // recessed/stroke/letterpress
            }
        }
        return New(hf, heights);
    }

    /// <summary>Split at a horizontal plane: keep the part ABOVE planeHeight, re-based to 0.</summary>
    public static HeightfieldData Split(HeightfieldData hf, double planeHeight)
    {
        var above = hf.Heights.Select(h => Math.Max(0, h - planeHeight)).ToArray();
        double minAbove = above.Min();
        var rebased = above.Select(v => v - minAbove).ToArray();
        return New(hf, rebased);
    }

    private static HeightfieldData New(HeightfieldData hf, double[] heights)
        => new(hf.Width, hf.Height, hf.CellSizeMm, hf.MinX, hf.MinY, heights);
}
