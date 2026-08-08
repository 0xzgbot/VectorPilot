namespace VectorPilot.Engine;

public enum SculptTool { Brush, Inflate, Deflate, Flatten, Smooth, Pinch, Grab }
public enum BrushShape { Sphere, Cylinder, Flat, Custom }
public enum BrushFalloff { Linear, Smooth, Constant, Root }

/// <summary>One sculpt brush stroke (ported from SculptStrokeParams.swift, SPK-0713).</summary>
public sealed class SculptStrokeParams
{
    public SculptTool Tool { get; set; } = SculptTool.Brush;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusMm { get; set; } = 5.0;
    public double Strength { get; set; } = 0.5;   // signed −1..1
    public double MaxDeltaMm { get; set; } = 2.0;
    public BrushShape BrushShape { get; set; } = BrushShape.Sphere;
    public BrushFalloff BrushFalloff { get; set; } = BrushFalloff.Smooth;

    public void Clamp()
    {
        RadiusMm = Math.Max(0.1, RadiusMm);
        Strength = Math.Clamp(Strength, -1.0, 1.0);
        MaxDeltaMm = Math.Max(0.0, MaxDeltaMm);
    }
}

public sealed class SculptStrokeResult
{
    public HeightfieldData Heightfield { get; init; }
    public int CellsAffected { get; init; }
    public double MinHeight { get; init; }
    public double MaxHeight { get; init; }
}

/// <summary>
/// Pure heightfield sculpting (ported from SculptEngine.swift): every tool
/// returns a NEW HeightfieldData, touching only cells inside the brush radius.
/// brush/grab: h += strength·w·maxDelta; inflate/deflate: signed magnitude;
/// flatten: pull to footprint mean; smooth: blend to 4-neighbour average;
/// pinch: pull toward the center height. Heights clamp ≥ 0.
/// </summary>
public static class SculptEngine
{
    /// <summary>Falloff weight at normalized distance t ∈ [0,1] (0 = center).</summary>
    public static double FalloffWeight(double t, BrushShape shape, BrushFalloff falloff)
    {
        double tt = Math.Clamp(t, 0.0, 1.0);
        double profile = shape switch
        {
            BrushShape.Sphere => Math.Sqrt(1 - tt * tt),
            _ => 1.0
        };
        double edge = falloff switch
        {
            BrushFalloff.Linear => 1 - tt,
            BrushFalloff.Smooth => SmoothStep(1 - tt),
            BrushFalloff.Constant => 1.0,
            BrushFalloff.Root => Math.Sqrt(1 - tt),
            _ => 1 - tt
        };
        return profile * edge;
    }

    public static SculptStrokeResult ApplyStroke(SculptStrokeParams stroke, HeightfieldData hf)
    {
        if (hf.Width <= 0 || hf.Height <= 0 || hf.Heights.Length != hf.Width * hf.Height)
        {
            return new SculptStrokeResult { Heightfield = hf, CellsAffected = 0, MinHeight = hf.Heights.Min(), MaxHeight = hf.MaxHeight };
        }

        stroke.Clamp();
        var out_ = (double[])hf.Heights.Clone();
        int affected = 0;
        double r = stroke.RadiusMm;
        double cell = hf.CellSizeMm;

        int minCellX = Math.Max(0, (int)(((stroke.CenterX - r) - hf.MinX) / cell));
        int maxCellX = Math.Min(hf.Width - 1, (int)(((stroke.CenterX + r) - hf.MinX) / cell));
        int minCellY = Math.Max(0, (int)(((stroke.CenterY - r) - hf.MinY) / cell));
        int maxCellY = Math.Min(hf.Height - 1, (int)(((stroke.CenterY + r) - hf.MinY) / cell));

        double footprintSum = 0;
        int footprintCount = 0;
        double centerHeight = 0;
        bool foundCenter = false;
        if (stroke.Tool == SculptTool.Flatten)
        {
            for (int j = minCellY; j <= maxCellY; j++)
                for (int i = minCellX; i <= maxCellX; i++)
                {
                    footprintSum += out_[j * hf.Width + i];
                    footprintCount++;
                }
        }
        if (stroke.Tool == SculptTool.Pinch)
        {
            var ch = hf.HeightAt(stroke.CenterX, stroke.CenterY);
            if (ch is { } c) { centerHeight = c; foundCenter = true; }
        }
        double footprintMean = footprintCount > 0 ? footprintSum / footprintCount : 0;

        for (int j = minCellY; j <= maxCellY; j++)
        {
            for (int i = minCellX; i <= maxCellX; i++)
            {
                double cx = hf.MinX + (i + 0.5) * cell;
                double cy = hf.MinY + (j + 0.5) * cell;
                double dist = Math.Sqrt(Math.Pow(cx - stroke.CenterX, 2) + Math.Pow(cy - stroke.CenterY, 2));
                if (dist > r) continue;

                double w = FalloffWeight(dist / r, stroke.BrushShape, stroke.BrushFalloff);
                if (w <= 1e-9) continue;
                int idx = j * hf.Width + i;
                double h = out_[idx];
                double strength = stroke.Strength;
                double delta = stroke.MaxDeltaMm;

                switch (stroke.Tool)
                {
                    case SculptTool.Brush:
                    case SculptTool.Grab:
                        out_[idx] = Math.Max(0, h + strength * w * delta);
                        break;
                    case SculptTool.Inflate:
                        out_[idx] = Math.Max(0, h + Math.Abs(strength) * w * delta);
                        break;
                    case SculptTool.Deflate:
                        out_[idx] = Math.Max(0, h - Math.Abs(strength) * w * delta);
                        break;
                    case SculptTool.Flatten:
                        out_[idx] = Math.Max(0, h + (footprintMean - h) * Math.Abs(strength) * w);
                        break;
                    case SculptTool.Smooth:
                        double avg = LocalAverage(out_, hf, i, j);
                        out_[idx] = Math.Max(0, h + (avg - h) * Math.Abs(strength) * w);
                        break;
                    case SculptTool.Pinch:
                        if (foundCenter)
                        {
                            out_[idx] = Math.Max(0, h + (centerHeight - h) * Math.Abs(strength) * w);
                        }
                        break;
                }
                if (Math.Abs(out_[idx] - h) > 1e-12) affected++;
            }
        }

        return new SculptStrokeResult
        {
            Heightfield = new HeightfieldData(hf.Width, hf.Height, hf.CellSizeMm, hf.MinX, hf.MinY, out_),
            CellsAffected = affected,
            MinHeight = out_.Min(),
            MaxHeight = out_.Max()
        };
    }

    /// <summary>4-neighbour average; edge cells average their available neighbours.</summary>
    public static double LocalAverage(double[] heights, HeightfieldData hf, int i, int j)
    {
        double sum = 0;
        int n = 0;
        foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int ni = i + di, nj = j + dj;
            if (ni >= 0 && ni < hf.Width && nj >= 0 && nj < hf.Height)
            {
                sum += heights[nj * hf.Width + ni];
                n++;
            }
        }
        return n == 0 ? heights[j * hf.Width + i] : sum / n;
    }

    private static double SmoothStep(double u) => u * u * (3 - 2 * u);
}
