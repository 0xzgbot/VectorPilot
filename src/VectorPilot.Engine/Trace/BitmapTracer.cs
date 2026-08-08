using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Bitmap → vector contour tracer. Thresholds grayscale pixels (>= Threshold is
/// inside) and extracts closed outline polylines with marching squares.
/// Saddle cells (cases 5/10) are disambiguated by the cell-center pixel so the
/// inside region stays connected. The image is padded with one outside row/col
/// of "outside" pixels so every contour is a closed loop.
/// </summary>
public static class BitmapTracer
{
    /// <summary>
    /// Trace contours. `pixels` is row-major grayscale (0-255), width x height.
    /// Returns closed polylines in pixel coordinates (0..width, 0..height).
    /// </summary>
    public static List<VectorShape> Trace(byte[] pixels, int width, int height, byte threshold = 128)
    {
        var contours = new List<VectorShape>();
        if (pixels.Length < width * height || width < 2 || height < 2) return contours;

        // Padded grid: (width+2) x (height+2); border = outside.
        int pw = width + 2, ph = height + 2;
        var inside = new bool[pw * ph];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                inside[(y + 1) * pw + (x + 1)] = pixels[y * width + x] >= threshold;
            }
        }

        // Emit segments for every cell.
        var segments = new List<(double X1, double Y1, double X2, double Y2)>();
        for (int y = 0; y < ph - 1; y++)
        {
            for (int x = 0; x < pw - 1; x++)
            {
                int c00 = inside[y * pw + x] ? 1 : 0;          // bottom-left
                int c10 = inside[y * pw + x + 1] ? 1 : 0;      // bottom-right
                int c11 = inside[(y + 1) * pw + x + 1] ? 1 : 0; // top-right
                int c01 = inside[(y + 1) * pw + x] ? 1 : 0;     // top-left
                int cell = c00 | (c10 << 1) | (c11 << 2) | (c01 << 3);

                double px = x, py = y;
                // Edge midpoints (cell-relative): bottom, right, top, left.
                var midB = (px + 0.5, py);
                var midR = (px + 1, py + 0.5);
                var midT = (px + 0.5, py + 1);
                var midL = (px, py + 0.5);

                switch (cell)
                {
                    case 1: // only c00 inside: bottom-left corner
                        Seg(midB, midL); break;
                    case 2:
                        Seg(midB, midR); break;
                    case 3:
                        Seg(midL, midR); break;
                    case 4:
                        Seg(midR, midT); break;
                    case 5: // saddle: c10 + c01 inside
                        if (CenterInside(c00, c10, c11, c01)) { Seg(midB, midL); Seg(midR, midT); }
                        else { Seg(midB, midR); Seg(midT, midL); }
                        break;
                    case 6:
                        Seg(midB, midT); break;
                    case 7:
                        Seg(midT, midR); break;
                    case 8:
                        Seg(midL, midT); break;
                    case 9:
                        Seg(midB, midT); break;
                    case 10: // saddle: c00 + c11 inside
                        if (CenterInside(c00, c10, c11, c01)) { Seg(midB, midR); Seg(midT, midL); }
                        else { Seg(midB, midL); Seg(midR, midT); }
                        break;
                    case 11:
                        Seg(midT, midL); break;
                    case 12:
                        Seg(midL, midR); break;
                    case 13:
                        Seg(midB, midR); break;
                    case 14:
                        Seg(midB, midL); break;
                    // 0 and 15: no crossing
                }
            }
        }

        ChainSegments(segments, contours);
        return contours;

        void Seg((double X, double Y) a, (double X, double Y) b)
        {
            // Orient so chaining is deterministic (start lexicographically smaller).
            if (a.X < b.X || (Math.Abs(a.X - b.X) < 1e-9 && a.Y < b.Y))
            {
                segments.Add((a.X, a.Y, b.X, b.Y));
            }
            else
            {
                segments.Add((b.X, b.Y, a.X, a.Y));
            }
        }
    }

    /// <summary>Saddle disambiguation: the virtual cell-center sample takes the
    /// majority of the four corners (deterministic; keeps the inside connected).</summary>
    private static bool CenterInside(int c00, int c10, int c11, int c01)
        => c00 + c10 + c11 + c01 >= 2;

    /// <summary>Chain raw segments into closed polylines by shared endpoints.</summary>
    private static void ChainSegments(List<(double X1, double Y1, double X2, double Y2)> segments, List<VectorShape> contours)
    {
        if (segments.Count == 0) return;
        var remaining = new HashSet<int>(Enumerable.Range(0, segments.Count));
        var byPoint = new Dictionary<(long, long), List<int>>();

        long Key(double v) => (long)Math.Round(v * 1e6);
        foreach (var (idx, seg) in segments.Select((s, i) => (i, s)))
        {
            AddPoint((Key(seg.X1), Key(seg.Y1)), idx);
            AddPoint((Key(seg.X2), Key(seg.Y2)), idx);
        }

        while (remaining.Count > 0)
        {
            int startIdx = remaining.First();
            var pts = new List<VectorPoint>();
            var start = (Key(segments[startIdx].X1), Key(segments[startIdx].Y1));
            var current = start;
            int currentIdx = startIdx;
            var used = new HashSet<int> { startIdx };

            pts.Add(new VectorPoint(segments[startIdx].X1, segments[startIdx].Y1));
            pts.Add(new VectorPoint(segments[startIdx].X2, segments[startIdx].Y2));
            current = (Key(segments[startIdx].X2), Key(segments[startIdx].Y2));

            while (true)
            {
                var next = FindNext(byPoint, current, currentIdx, used);
                if (next < 0) break; // open chain (shouldn't happen with padding)
                used.Add(next);
                // Continuation point: the segment's other endpoint.
                var seg = segments[next];
                var cont = (Key(seg.X1), Key(seg.Y1)) == current
                    ? new VectorPoint(seg.X2, seg.Y2)
                    : new VectorPoint(seg.X1, seg.Y1);
                if ((Key(cont.X), Key(cont.Y)) == start) break; // loop closed
                pts.Add(cont);
                current = (Key(cont.X), Key(cont.Y));
                currentIdx = next;
            }

            foreach (var idx in used) remaining.Remove(idx);
            if (pts.Count >= 3 && pts[0].DistanceTo(pts[^1]) > 1e-6) pts.Add(pts[0]);
            if (pts.Count >= 4)
            {
                contours.Add(VectorShape.Polyline(pts, closed: true));
            }
        }

        void AddPoint((long, long) key, int idx)
        {
            if (!byPoint.TryGetValue(key, out var list)) byPoint[key] = list = new List<int>();
            list.Add(idx);
        }
    }

    private static int FindNext(Dictionary<(long, long), List<int>> byPoint, (long, long) key, int fromIdx, HashSet<int> used)
    {
        if (!byPoint.TryGetValue(key, out var candidates)) return -1;
        foreach (var idx in candidates)
        {
            if (idx == fromIdx || used.Contains(idx)) continue;
            return idx;
        }
        return -1;
    }
}
