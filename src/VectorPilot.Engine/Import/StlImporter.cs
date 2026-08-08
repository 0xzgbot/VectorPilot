using System.Globalization;
using System.Text;

namespace VectorPilot.Engine;

/// <summary>Import result for STL/OBJ/3MF heightfield importers (mirrors the Swift result shapes).</summary>
public sealed class HeightfieldImportResult
{
    public HeightfieldData? Heightfield { get; init; }
    public int TriangleCount { get; init; }
    public int VertexCount { get; init; }
    public int FaceCount { get; init; }
    public long FileSizeBytes { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// STL heightfield importer (ported from STLHeightfieldImporter.swift): ASCII STL
/// (plus binary STL, which the Swift skips) rasterized to the top-surface grid.
/// </summary>
public static class StlImporter
{
    /// <summary>A single 3D triangle.</summary>
    public readonly record struct Triangle((double X, double Y, double Z) A, (double X, double Y, double Z) B, (double X, double Y, double Z) C);

    public static HeightfieldImportResult Import(byte[] data, string sourceName = "STL", double cellSizeMm = 1.0, double scale = 1.0)
    {
        var triangles = Parse(data);
        if (triangles.Count == 0)
        {
            return new HeightfieldImportResult
            {
                TriangleCount = 0, FileSizeBytes = data.Length, Success = false,
                ErrorMessage = "STL contains no valid triangles"
            };
        }
        var grid = Rasterize(triangles, cellSizeMm, scale);
        return new HeightfieldImportResult
        {
            Heightfield = grid, TriangleCount = triangles.Count, FaceCount = triangles.Count,
            VertexCount = triangles.Count * 3, FileSizeBytes = data.Length, Success = true
        };
    }

    /// <summary>Parse ASCII or binary STL bytes into triangles. Never throws.</summary>
    public static List<Triangle> Parse(byte[] data)
    {
        if (data.Length < 5) return new List<Triangle>();
        // Binary STL: 80-byte header + uint32 count; if the count matches the
        // remaining byte length (count * 50) it is binary regardless of "solid".
        var triangles = new List<Triangle>();
        if (data.Length >= 84)
        {
            uint count = BitConverter.ToUInt32(data, 80);
            if (84 + count * 50L <= data.Length)
            {
                return ParseBinary(data, (int)count);
            }
        }

        var text = Encoding.UTF8.GetString(data);
        if (!text.Contains("vertex", StringComparison.OrdinalIgnoreCase)) return triangles;

        var verts = new List<(double X, double Y, double Z)>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 4 && tokens[0].Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                if (TryNum(tokens[1], out var x) && TryNum(tokens[2], out var y) && TryNum(tokens[3], out var z))
                {
                    verts.Add((x, y, z));
                }
            }
        }

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            AddIfSolid(triangles, verts[i], verts[i + 1], verts[i + 2]);
        }
        return triangles;
    }

    private static List<Triangle> ParseBinary(byte[] data, int count)
    {
        var triangles = new List<Triangle>();
        int offset = 84;
        for (int i = 0; i < count && offset + 50 <= data.Length; i++)
        {
            // normal (12 bytes, ignored), 3 vertices (36 bytes), attr (2 bytes)
            var a = ReadVec(data, offset + 12);
            var b = ReadVec(data, offset + 24);
            var c = ReadVec(data, offset + 36);
            AddIfSolid(triangles, a, b, c);
            offset += 50;
        }
        return triangles;
    }

    private static (double X, double Y, double Z) ReadVec(byte[] d, int o)
        => (BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));

    private static void AddIfSolid(List<Triangle> tris, (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c)
    {
        var ab = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        var ac = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        double nx = ab.Item2 * ac.Item3 - ab.Item3 * ac.Item2;
        double ny = ab.Item3 * ac.Item1 - ab.Item1 * ac.Item3;
        double nz = ab.Item1 * ac.Item2 - ab.Item2 * ac.Item1;
        if (Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz) > 1e-12)
        {
            tris.Add(new Triangle(a, b, c));
        }
    }

    /// <summary>
    /// Rasterize the triangle soup onto a heightfield grid: each cell takes the MAX Z of
    /// every triangle whose XY projection covers the cell center (top surface).
    /// Ported from STLHeightfieldImporter.rasterize.
    /// </summary>
    public static HeightfieldData Rasterize(List<Triangle> triangles, double cellSizeMm, double scale)
    {
        if (triangles.Count == 0 || cellSizeMm <= 1e-9)
        {
            return new HeightfieldData(1, 1, cellSizeMm, 0, 0, new double[] { 0 });
        }
        double s = scale > 0 ? scale : 1.0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var t in triangles)
        {
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                minX = Math.Min(minX, v.X * s); maxX = Math.Max(maxX, v.X * s);
                minY = Math.Min(minY, v.Y * s); maxY = Math.Max(maxY, v.Y * s);
            }
        }
        if (!(maxX > minX) || !(maxY > minY))
        {
            return new HeightfieldData(1, 1, cellSizeMm, minX, minY, new double[] { 0 });
        }

        int width = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cellSizeMm));
        int height = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cellSizeMm));
        const int maxCells = 600;
        double cellX = width > maxCells ? (maxX - minX) / maxCells : cellSizeMm;
        double cellY = height > maxCells ? (maxY - minY) / maxCells : cellSizeMm;
        int gx = width > maxCells ? maxCells : width;
        int gy = height > maxCells ? maxCells : height;

        var heights = new double[gx * gy];
        foreach (var t in triangles)
        {
            var a = (t.A.X * s, t.A.Y * s, t.A.Z * s);
            var b = (t.B.X * s, t.B.Y * s, t.B.Z * s);
            var c = (t.C.X * s, t.C.Y * s, t.C.Z * s);
            double triMinX = Math.Min(a.Item1, Math.Min(b.Item1, c.Item1));
            double triMaxX = Math.Max(a.Item1, Math.Max(b.Item1, c.Item1));
            double triMinY = Math.Min(a.Item2, Math.Min(b.Item2, c.Item2));
            double triMaxY = Math.Max(a.Item2, Math.Max(b.Item2, c.Item2));

            var ab = (b.Item1 - a.Item1, b.Item2 - a.Item2, b.Item3 - a.Item3);
            var ac = (c.Item1 - a.Item1, c.Item2 - a.Item2, c.Item3 - a.Item3);
            double nx = ab.Item2 * ac.Item3 - ab.Item3 * ac.Item2;
            double ny = ab.Item3 * ac.Item1 - ab.Item1 * ac.Item3;
            double nz = ab.Item1 * ac.Item2 - ab.Item2 * ac.Item1;
            double nzAbs = Math.Abs(nz);

            int startX = Math.Max(0, (int)((triMinX - minX) / cellX) - 1);
            int endX = Math.Min(gx - 1, (int)((triMaxX - minX) / cellX) + 1);
            int startY = Math.Max(0, (int)((triMinY - minY) / cellY) - 1);
            int endY = Math.Min(gy - 1, (int)((triMaxY - minY) / cellY) + 1);

            for (int row = startY; row <= endY; row++)
            {
                double cy = minY + (row + 0.5) * cellY;
                for (int col = startX; col <= endX; col++)
                {
                    double cx = minX + (col + 0.5) * cellX;
                    if (!PointInTriangle(cx, cy, a, b, c)) continue;
                    double z;
                    if (nzAbs > 1e-12)
                    {
                        z = a.Item3 - (nx * (cx - a.Item1) + ny * (cy - a.Item2)) / nz;
                    }
                    else
                    {
                        z = Math.Max(a.Item3, Math.Max(b.Item3, c.Item3));
                    }
                    int idx = row * gx + col;
                    if (z > heights[idx]) heights[idx] = z;
                }
            }
        }
        return new HeightfieldData(gx, gy, Math.Min(cellX, cellY), minX, minY, heights);
    }

    /// <summary>Half-plane point-in-triangle test (winding-agnostic).</summary>
    public static bool PointInTriangle(double px, double py, (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c)
    {
        static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;
        double d1 = Cross(b.X - a.X, b.Y - a.Y, px - a.X, py - a.Y);
        double d2 = Cross(c.X - b.X, c.Y - b.Y, px - b.X, py - b.Y);
        double d3 = Cross(a.X - c.X, a.Y - c.Y, px - c.X, py - c.Y);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
