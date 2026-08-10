namespace VectorPilot.Engine;

/// <summary>
/// Model offset engine (ported from ModelOffsetEngine.swift, parity row E22):
/// dilates/erodes the solid form of a heightfield via chamfer distance
/// transforms on the material mask. Deterministic, dependency-free.
/// </summary>
public static class ModelOffsetEngine
{
    public sealed class OffsetParams
    {
        /// Positive = expand; negative = inset; zero = identity.
        public double OffsetMm { get; set; }
        /// Reserved (future taper pass); kept for form-field parity.
        public double TaperDegrees { get; set; }
    }

    public sealed class OffsetResult
    {
        public HeightfieldData Heightfield { get; init; } = null!;
        public int ChangedCellCount { get; init; }
        public double MaxHeightAfter { get; init; }
    }

    private const double Epsilon = 1e-6;
    private const double Inf = double.MaxValue;

    public static OffsetResult? Offset(HeightfieldData heightfield, OffsetParams p)
    {
        int w = heightfield.Width, h = heightfield.Height;
        int n = w * h;
        if (n == 0 || heightfield.Heights.Length != n) return null;
        if (Math.Abs(p.OffsetMm) <= Epsilon)
        {
            return new OffsetResult { Heightfield = heightfield, ChangedCellCount = 0, MaxHeightAfter = heightfield.MaxHeight };
        }

        var heights = heightfield.Heights;
        double floor = heights.Min();

        // Material mask: cells above the floor.
        var material = new bool[n];
        bool anyMaterial = false, anyFloor = false;
        for (int i = 0; i < n; i++)
        {
            material[i] = heights[i] > floor + Epsilon;
            if (material[i]) anyMaterial = true; else anyFloor = true;
        }
        if (!anyMaterial || !anyFloor)
        {
            return new OffsetResult { Heightfield = heightfield, ChangedCellCount = 0, MaxHeightAfter = heights.Max() };
        }

        double bandCells = Math.Abs(p.OffsetMm) / Math.Max(1e-9, heightfield.CellSizeMm);
        var out_ = (double[])heights.Clone();
        int changed = 0;

        if (p.OffsetMm > 0)
        {
            // DILATION: non-material cells within the band raise to nearest material height.
            var distToMaterial = Enumerable.Repeat(Inf, n).ToArray();
            for (int i = 0; i < n; i++) if (material[i]) distToMaterial[i] = 0;
            ChamferSweep(distToMaterial, w, h, material, seedIsMaterial: true);

            var nearestHeight = Enumerable.Repeat(floor, n).ToArray();
            for (int i = 0; i < n; i++) if (material[i]) nearestHeight[i] = heights[i];
            PropagateHeights(nearestHeight, w, h, material);

            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    int idx = j * w + i;
                    if (material[idx]) continue;
                    double d = distToMaterial[idx];
                    if (d > bandCells) continue;
                    double target = nearestHeight[idx];
                    if (Math.Abs(target - heights[idx]) > Epsilon)
                    {
                        out_[idx] = target;
                        changed++;
                    }
                }
            }
        }
        else
        {
            // EROSION: material cells within the band lower toward the floor.
            var distToBoundary = Enumerable.Repeat(Inf, n).ToArray();
            for (int i = 0; i < n; i++) if (!material[i]) distToBoundary[i] = 0;
            ChamferSweep(distToBoundary, w, h, material, seedIsMaterial: false);

            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    int idx = j * w + i;
                    if (!material[idx]) continue;
                    double d = distToBoundary[idx];
                    if (d > bandCells) continue;
                    double t = bandCells <= 1.0 ? 0 : Math.Max(0, (d - 1.0) / (bandCells - 1.0));
                    double target = floor + (heights[idx] - floor) * t;
                    if (Math.Abs(target - heights[idx]) > Epsilon)
                    {
                        out_[idx] = target;
                        changed++;
                    }
                }
            }
        }

        return new OffsetResult
        {
            Heightfield = new HeightfieldData(w, h, heightfield.CellSizeMm, heightfield.MinX, heightfield.MinY, out_),
            ChangedCellCount = changed,
            MaxHeightAfter = out_.Max()
        };
    }

    /// <summary>Two-pass chamfer distance transform (1.0 orthogonal / 1.414 diagonal).</summary>
    private static void ChamferSweep(double[] dist, int w, int h, bool[] material, bool seedIsMaterial)
    {
        bool Seeded(int i, int j)
        {
            if (i < 0 || i >= w || j < 0 || j >= h) return false;
            return seedIsMaterial ? material[j * w + i] : !material[j * w + i];
        }

        // Forward pass (top-left → bottom-right).
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                if (Seeded(i, j)) continue;
                double best = dist[j * w + i];
                foreach (var (di, dj, wgt) in new[] { (-1, -1, 1.414), (0, -1, 1.0), (-1, 0, 1.0), (1, -1, 1.414) })
                {
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && ni < w && nj >= 0 && nj < h && !Seeded(ni, nj))
                    {
                        best = Math.Min(best, dist[nj * w + ni] + wgt);
                    }
                }
                if (!seedIsMaterial && (i == 0 || j == 0 || i == w - 1 || j == h - 1))
                {
                    best = Math.Min(best, 1.0);
                }
                foreach (var (di, dj, wgt) in new[] { (-1, 0, 1.0), (0, -1, 1.0), (-1, -1, 1.414) })
                {
                    int ni = i + di, nj = j + dj;
                    if (Seeded(ni, nj)) best = Math.Min(best, wgt);
                }
                dist[j * w + i] = best == Inf ? 1.0 : best;
            }
        }

        // Backward pass (bottom-right → top-left).
        for (int j = h - 1; j >= 0; j--)
        {
            for (int i = w - 1; i >= 0; i--)
            {
                if (Seeded(i, j)) continue;
                double best = dist[j * w + i];
                foreach (var (di, dj, wgt) in new[] { (1, 1, 1.414), (0, 1, 1.0), (1, 0, 1.0), (-1, 1, 1.414) })
                {
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && ni < w && nj >= 0 && nj < h && !Seeded(ni, nj))
                    {
                        best = Math.Min(best, dist[nj * w + ni] + wgt);
                    }
                }
                foreach (var (di, dj, wgt) in new[] { (1, 0, 1.0), (0, 1, 1.0), (1, 1, 1.414) })
                {
                    int ni = i + di, nj = j + dj;
                    if (Seeded(ni, nj)) best = Math.Min(best, wgt);
                }
                dist[j * w + i] = best;
            }
        }
    }

    /// <summary>Propagate material heights to non-material cells (max-merge).</summary>
    private static void PropagateHeights(double[] heights, int w, int h, bool[] material)
    {
        bool Seeded(int i, int j)
        {
            if (i < 0 || i >= w || j < 0 || j >= h) return false;
            return material[j * w + i];
        }

        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                if (Seeded(i, j)) continue;
                double best = heights[j * w + i];
                foreach (var (di, dj) in new[] { (-1, -1), (0, -1), (-1, 0), (1, -1) })
                {
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && ni < w && nj >= 0 && nj < h)
                    {
                        best = Math.Max(best, heights[nj * w + ni]);
                    }
                }
                heights[j * w + i] = best;
            }
        }

        for (int j = h - 1; j >= 0; j--)
        {
            for (int i = w - 1; i >= 0; i--)
            {
                if (Seeded(i, j)) continue;
                double best = heights[j * w + i];
                foreach (var (di, dj) in new[] { (1, 1), (0, 1), (1, 0), (-1, 1) })
                {
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && ni < w && nj >= 0 && nj < h)
                    {
                        best = Math.Max(best, heights[nj * w + ni]);
                    }
                }
                heights[j * w + i] = best;
            }
        }
    }
}
