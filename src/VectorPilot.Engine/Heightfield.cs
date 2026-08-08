namespace VectorPilot.Engine;

/// <summary>
/// A 2.5D relief grid (ported from HeightfieldData.swift, SPK-3D-spine-a).
/// Row-major heights, world origin at (MinX, MinY); each cell is CellSizeMm square.
/// </summary>
public sealed class HeightfieldData
{
    public int Width { get; }        // cells along X
    public int Height { get; }       // cells along Y
    public double CellSizeMm { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double[] Heights { get; } // row-major, count == Width * Height

    public HeightfieldData(int width, int height, double cellSizeMm, double minX, double minY, double[] heights)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        CellSizeMm = cellSizeMm;
        MinX = minX;
        MinY = minY;
        Heights = heights.Length == Width * Height ? heights : new double[Width * Height];
    }

    /// <summary>Height at a world coordinate (nearest cell), or null outside the grid.</summary>
    public double? HeightAt(double x, double y)
    {
        if (CellSizeMm <= 1e-9) return null;
        if (x < MinX || y < MinY || x >= MinX + (double)Width * CellSizeMm || y >= MinY + (double)Height * CellSizeMm)
        {
            return null;
        }
        int gx = (int)((x - MinX) / CellSizeMm);
        int gy = (int)((y - MinY) / CellSizeMm);
        return Heights[gy * Width + gx];
    }

    /// <summary>Bilinear sample treating each cell value as its center sample. Outside grid → 0.</summary>
    public double HeightInterpolated(double x, double y)
    {
        if (CellSizeMm <= 1e-9 || Width < 2 || Height < 2)
        {
            return HeightAt(x, y) ?? 0;
        }
        double fx = (x - MinX) / CellSizeMm - 0.5;
        double fy = (y - MinY) / CellSizeMm - 0.5;
        if (fx < 0 || fy < 0 || fx > Width - 1 || fy > Height - 1) return 0;
        int i0 = Math.Min(Width - 2, (int)fx);
        int j0 = Math.Min(Height - 2, (int)fy);
        double tx = fx - i0, ty = fy - j0;
        double h00 = Heights[j0 * Width + i0];
        double h10 = Heights[j0 * Width + i0 + 1];
        double h01 = Heights[(j0 + 1) * Width + i0];
        double h11 = Heights[(j0 + 1) * Width + i0 + 1];
        double top = h00 + (h10 - h00) * tx;
        double bottom = h01 + (h11 - h01) * tx;
        return top + (bottom - top) * ty;
    }

    public double MaxHeight => Heights.Length == 0 ? 0 : Heights.Max();

    public (double MinX, double MinY, double MaxX, double MaxY) Bounds
        => (MinX, MinY, MinX + (double)Width * CellSizeMm, MinY + (double)Height * CellSizeMm);
}
