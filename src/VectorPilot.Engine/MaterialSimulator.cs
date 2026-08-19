using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>
/// Material removal simulation (card P4). Sweeps the posted G-code through a
/// height grid representing the stock, lowering every cell the cutter passes
/// over. The result is what the part actually looks like after the program runs —
/// which catches gouges, missed regions, and over-deep cuts before the spindle
/// turns.
/// </summary>
public static class MaterialSimulator
{
    public sealed class Result
    {
        /// <summary>Remaining stock height per cell (mm), row-major.</summary>
        public required HeightfieldData Stock { get; init; }

        /// <summary>Volume removed (mm³).</summary>
        public double RemovedVolumeMm3 { get; init; }

        /// <summary>Fraction of the stock surface the cutter touched (0-1).</summary>
        public double CoverageFraction { get; init; }

        /// <summary>Deepest point reached below the top surface (mm, positive).</summary>
        public double MaxCutDepthMm { get; init; }

        /// <summary>True when any cut went below the stock's bottom face.</summary>
        public bool CutThrough { get; init; }
    }

    /// <summary>
    /// Simulate a program against a rectangular blank.
    /// </summary>
    /// <param name="gcode">Posted lines (G0/G1 motion is used; others ignored).</param>
    /// <param name="stockWidth">Blank X extent (mm).</param>
    /// <param name="stockHeight">Blank Y extent (mm).</param>
    /// <param name="stockThickness">Blank Z thickness (mm); top surface is Z=0.</param>
    /// <param name="toolDiameter">Cutter diameter (mm).</param>
    /// <param name="cellSizeMm">Simulation grid resolution.</param>
    public static Result Simulate(
        IReadOnlyList<string> gcode,
        double stockWidth,
        double stockHeight,
        double stockThickness,
        double toolDiameter = 6.35,
        double cellSizeMm = 1.0)
    {
        int nx = Math.Max(2, (int)Math.Round(stockWidth / cellSizeMm));
        int ny = Math.Max(2, (int)Math.Round(stockHeight / cellSizeMm));

        // Every cell starts at the top of the blank (Z = 0); cuts go negative.
        var heights = new double[nx * ny];
        var touched = new bool[nx * ny];

        double radius = Math.Max(toolDiameter / 2.0, cellSizeMm / 2.0);
        double x = 0, y = 0, z = 0;
        bool have = false;
        double deepest = 0;

        foreach (var raw in gcode)
        {
            var line = raw.Split(';')[0].Split('(')[0].Trim();
            if (line.Length == 0) continue;

            bool motion = line.StartsWith("G0", StringComparison.OrdinalIgnoreCase)
                       || line.StartsWith("G1", StringComparison.OrdinalIgnoreCase);
            if (!motion) continue;

            double nxPos = x, nyPos = y, nzPos = z;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    continue;
                switch (char.ToUpperInvariant(tok[0]))
                {
                    case 'X': nxPos = v; break;
                    case 'Y': nyPos = v; break;
                    case 'Z': nzPos = v; break;
                }
            }

            // Only cutting moves remove material, and only below the top surface.
            if (have && (nzPos < 0 || z < 0))
                SweepSegment(heights, touched, nx, ny, cellSizeMm,
                             x, y, z, nxPos, nyPos, nzPos, radius, ref deepest);

            x = nxPos; y = nyPos; z = nzPos;
            have = true;
        }

        double cellArea = cellSizeMm * cellSizeMm;
        double removed = 0;
        int touchedCount = 0;
        for (int i = 0; i < heights.Length; i++)
        {
            removed += -heights[i] * cellArea;   // heights are <= 0
            if (touched[i]) touchedCount++;
        }

        return new Result
        {
            Stock = new HeightfieldData(nx, ny, cellSizeMm, 0, 0, heights),
            RemovedVolumeMm3 = removed,
            CoverageFraction = heights.Length == 0 ? 0 : (double)touchedCount / heights.Length,
            MaxCutDepthMm = deepest,
            CutThrough = deepest > stockThickness + 1e-9
        };
    }

    /// <summary>
    /// Lower every cell within the tool radius of the segment, interpolating Z.
    /// </summary>
    private static void SweepSegment(
        double[] heights, bool[] touched, int nx, int ny, double cell,
        double x0, double y0, double z0, double x1, double y1, double z1,
        double radius, ref double deepest)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double len = Math.Sqrt(dx * dx + dy * dy);

        // Step along the segment at half-cell resolution so no cell is skipped.
        int steps = Math.Max(1, (int)Math.Ceiling(len / (cell * 0.5)));
        for (int s = 0; s <= steps; s++)
        {
            double t = steps == 0 ? 0 : (double)s / steps;
            double px = x0 + dx * t;
            double py = y0 + dy * t;
            double pz = z0 + (z1 - z0) * t;
            if (pz >= 0) continue;                    // above the stock: no cut

            Stamp(heights, touched, nx, ny, cell, px, py, pz, radius, ref deepest);
        }
    }

    /// <summary>Press a flat-bottomed cutter of the given radius into the grid.</summary>
    private static void Stamp(
        double[] heights, bool[] touched, int nx, int ny, double cell,
        double px, double py, double pz, double radius, ref double deepest)
    {
        int i0 = (int)Math.Floor((px - radius) / cell);
        int i1 = (int)Math.Ceiling((px + radius) / cell);
        int j0 = (int)Math.Floor((py - radius) / cell);
        int j1 = (int)Math.Ceiling((py + radius) / cell);

        for (int j = Math.Max(0, j0); j <= Math.Min(ny - 1, j1); j++)
        {
            double cy = (j + 0.5) * cell;
            for (int i = Math.Max(0, i0); i <= Math.Min(nx - 1, i1); i++)
            {
                double cx = (i + 0.5) * cell;
                double ddx = cx - px, ddy = cy - py;
                if (ddx * ddx + ddy * ddy > radius * radius) continue;

                int idx = j * nx + i;
                if (pz < heights[idx]) heights[idx] = pz;
                touched[idx] = true;
                if (-pz > deepest) deepest = -pz;
            }
        }
    }
}
