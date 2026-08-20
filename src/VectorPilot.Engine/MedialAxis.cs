using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Discrete medial axis (skeleton) of a closed outline, from a clearance field.
///
/// Neither VectorPilot nor the Mac had one: V-carve sampled depth ALONG THE INPUT PATH,
/// so a closed pocket was only ever traced around its outline. The middle — where a
/// V-bit must actually plunge deepest — was never visited.
///
/// Method: rasterise a clearance field (distance from each interior cell to the nearest
/// boundary), keep the ridge cells (local maxima of clearance), and chain them into
/// polylines. Depth then comes from the clearance at each ridge point, which is exactly
/// the half-width the V-bit geometry needs.
/// </summary>
public static class MedialAxis
{
    public sealed class RidgePoint
    {
        public VectorPoint Position { get; init; }

        /// <summary>Distance to the nearest boundary — the local half-width.</summary>
        public double ClearanceMm { get; init; }
    }

    public sealed class Result
    {
        /// <summary>Ridge polylines, longest first.</summary>
        public List<List<RidgePoint>> Paths { get; init; } = new();

        public double MaxClearanceMm { get; init; }
        public bool IsEmpty => Paths.Count == 0;
    }

    /// <summary>
    /// Compute the skeleton of <paramref name="outline"/>.
    /// </summary>
    /// <param name="cellMm">Grid resolution. Smaller is more accurate and slower.</param>
    public static Result Compute(IReadOnlyList<VectorPoint> outline, double cellMm = 1.0)
    {
        if (outline.Count < 3) return new Result();

        double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
        double minY = outline.Min(p => p.Y), maxY = outline.Max(p => p.Y);

        double cell = Math.Max(0.05, cellMm);
        int nx = (int)Math.Ceiling((maxX - minX) / cell) + 1;
        int ny = (int)Math.Ceiling((maxY - minY) / cell) + 1;

        // Guard against a pathological grid on a huge shape with a tiny cell.
        const int MaxCells = 4_000_000;
        if ((long)nx * ny > MaxCells)
        {
            double scale = Math.Sqrt((double)nx * ny / MaxCells);
            cell *= scale;
            nx = (int)Math.Ceiling((maxX - minX) / cell) + 1;
            ny = (int)Math.Ceiling((maxY - minY) / cell) + 1;
        }

        // Clearance field: distance to the nearest boundary edge, 0 outside.
        var clearance = new double[nx, ny];
        double maxClear = 0;

        for (int ix = 0; ix < nx; ix++)
        {
            double x = minX + ix * cell;
            for (int iy = 0; iy < ny; iy++)
            {
                double y = minY + iy * cell;
                var p = new VectorPoint(x, y);
                if (!PointInPolygon(p, outline)) continue;

                double d = DistanceToBoundary(p, outline);
                clearance[ix, iy] = d;
                if (d > maxClear) maxClear = d;
            }
        }

        if (maxClear <= 0) return new Result();

        // Ridge cells: clearance is a local maximum along at least one axis, and deep
        // enough to matter (a full cell in from the wall).
        double floor = cell;
        var ridge = new List<(int X, int Y, double C)>();

        for (int ix = 1; ix < nx - 1; ix++)
            for (int iy = 1; iy < ny - 1; iy++)
            {
                double c = clearance[ix, iy];
                if (c <= floor) continue;

                bool ridgeX = c >= clearance[ix - 1, iy] && c >= clearance[ix + 1, iy];
                bool ridgeY = c >= clearance[ix, iy - 1] && c >= clearance[ix, iy + 1];
                bool ridgeD1 = c >= clearance[ix - 1, iy - 1] && c >= clearance[ix + 1, iy + 1];
                bool ridgeD2 = c >= clearance[ix - 1, iy + 1] && c >= clearance[ix + 1, iy - 1];

                // A ridge in one direction is a skeleton cell; requiring both axes would
                // keep only isolated peaks and lose the spine of a long channel.
                if (ridgeX || ridgeY || ridgeD1 || ridgeD2)
                    ridge.Add((ix, iy, c));
            }

        if (ridge.Count == 0) return new Result { MaxClearanceMm = maxClear };

        var paths = ChainRidge(ridge, minX, minY, cell);

        return new Result
        {
            Paths = paths.OrderByDescending(p => p.Count).ToList(),
            MaxClearanceMm = maxClear
        };
    }

    /// <summary>Greedy nearest-neighbour chaining of ridge cells into polylines.</summary>
    private static List<List<RidgePoint>> ChainRidge(
        List<(int X, int Y, double C)> ridge, double minX, double minY, double cell)
    {
        var remaining = new HashSet<(int, int)>(ridge.Select(r => (r.X, r.Y)));
        var lookup = ridge.ToDictionary(r => (r.X, r.Y), r => r.C);
        var paths = new List<List<RidgePoint>>();

        // Start from the widest point: that is the deepest part of the carve.
        foreach (var seed in ridge.OrderByDescending(r => r.C))
        {
            if (!remaining.Contains((seed.X, seed.Y))) continue;

            var path = new List<RidgePoint>();
            var current = (seed.X, seed.Y);

            while (true)
            {
                remaining.Remove(current);
                path.Add(new RidgePoint
                {
                    Position = new VectorPoint(minX + current.Item1 * cell, minY + current.Item2 * cell),
                    ClearanceMm = lookup[current]
                });

                // Walk to an 8-connected neighbour still on the ridge.
                (int, int)? next = null;
                for (int dx = -1; dx <= 1 && next is null; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var cand = (current.Item1 + dx, current.Item2 + dy);
                        if (remaining.Contains(cand)) { next = cand; break; }
                    }

                if (next is null) break;
                current = next.Value;
            }

            // A single cell is noise, not a path.
            if (path.Count >= 2) paths.Add(path);
        }

        return paths;
    }

    /// <summary>Shortest distance from a point to any edge of the outline.</summary>
    public static double DistanceToBoundary(VectorPoint p, IReadOnlyList<VectorPoint> poly)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            best = Math.Min(best, DistanceToSegment(p, a, b));
        }
        return best == double.MaxValue ? 0 : best;
    }

    private static double DistanceToSegment(VectorPoint p, VectorPoint a, VectorPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }

    /// <summary>Even-odd containment test.</summary>
    public static bool PointInPolygon(VectorPoint p, IReadOnlyList<VectorPoint> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }
}
