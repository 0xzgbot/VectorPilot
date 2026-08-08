using System.IO.Compression;
using System.Text;

namespace VectorPilot.Engine;

/// <summary>
/// Grayscale bitmap ↔ heightfield (Aspire "Export as Grayscale Bitmap" parity).
/// BMP (trivial, universally readable) and PNG (zlib IDAT via ZLibStream, CRC32).
/// </summary>
public static class GrayscaleBitmap
{
    // ---- Heightfield → grayscale ----

    /// <summary>Export a heightfield as a grayscale BMP (8-bit).</summary>
    public static byte[] ToBmp(HeightfieldData hf, double maxHeight = 0)
    {
        double max = maxHeight > 0 ? maxHeight : hf.MaxHeight;
        int w = hf.Width, h = hf.Height;
        int rowSize = (w + 3) & ~3; // 4-byte row alignment
        int pixelBytes = rowSize * h;
        int fileSize = 14 + 40 + pixelBytes;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // BITMAPFILEHEADER (14)
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write((ushort)0); bw.Write((ushort)0);
        bw.Write(54);
        // BITMAPINFOHEADER (40)
        bw.Write(40);
        bw.Write(w); bw.Write(h);
        bw.Write((ushort)1); bw.Write((ushort)8); // planes, bpp
        bw.Write(0); // no compression
        bw.Write(pixelBytes);
        bw.Write(2835); bw.Write(2835); // ppm
        bw.Write(0); bw.Write(0);       // palette colors
        // Grayscale palette (256 entries)
        for (int i = 0; i < 256; i++)
        {
            bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)0);
        }
        // Pixel rows, bottom-up
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                bw.Write((byte)Sample(hf, x, y, max));
            }
            for (int pad = w; pad < rowSize; pad++) bw.Write((byte)0);
        }
        return ms.ToArray();
    }

    /// <summary>Export a heightfield as a grayscale PNG (8-bit, no filter).</summary>
    public static byte[] ToPng(HeightfieldData hf, double maxHeight = 0)
    {
        double max = maxHeight > 0 ? maxHeight : hf.MaxHeight;
        int w = hf.Width, h = hf.Height;

        var raw = new byte[h * (w + 1)];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w + 1)] = 0; // filter: none
            for (int x = 0; x < w; x++)
            {
                raw[y * (w + 1) + 1 + x] = (byte)Sample(hf, x, y, max);
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }
        byte[] idatBytes = idat.ToArray();

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(ms, "IHDR", BE32(w).Concat(BE32(h)).Concat(new byte[] { 8, 0, 0, 0, 0 }).ToArray());
        WriteChunk(ms, "IDAT", idatBytes);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    // ---- Grayscale → heightfield ----

    /// <summary>Import a grayscale image (byte per pixel, row-major) as a heightfield.</summary>
    public static HeightfieldData FromGray(byte[] gray, int width, int height, double cellSizeMm, double minX, double minY, double maxHeight)
    {
        var heights = new double[width * height];
        for (int i = 0; i < gray.Length && i < heights.Length; i++)
        {
            heights[i] = gray[i] / 255.0 * maxHeight;
        }
        return new HeightfieldData(width, height, cellSizeMm, minX, minY, heights);
    }

    private static byte Sample(HeightfieldData hf, int x, int y, double max)
    {
        double v = hf.Heights[y * hf.Width + x];
        return max > 1e-9 ? (byte)Math.Clamp((int)Math.Round(v / max * 255.0), 0, 255) : (byte)0;
    }

    private static byte[] BE32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(BE32(data.Length));
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes);
        crc = Crc32Update(crc, data);
        s.Write(BE32((int)crc));
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Crc32Update(uint crc, byte[] bytes)
    {
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>Heightfield resolution math (Aspire modeling-resolution parity): resample
/// to a new cell size with bilinear interpolation.</summary>
public static class HeightfieldMath
{
    /// <summary>Resample to a new cell size. Returns null for degenerate inputs.</summary>
    public static HeightfieldData? Resample(HeightfieldData hf, double newCellSizeMm)
    {
        if (hf.CellSizeMm <= 1e-9 || newCellSizeMm <= 1e-9) return null;
        var (minX, minY, maxX, maxY) = hf.Bounds;
        int w = Math.Max(1, (int)Math.Round((maxX - minX) / newCellSizeMm));
        int h = Math.Max(1, (int)Math.Round((maxY - minY) / newCellSizeMm));
        var heights = new double[w * h];
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                double wx = minX + (i + 0.5) * newCellSizeMm;
                double wy = minY + (j + 0.5) * newCellSizeMm;
                heights[j * w + i] = hf.HeightInterpolated(wx, wy);
            }
        }
        return new HeightfieldData(w, h, newCellSizeMm, minX, minY, heights);
    }

    /// <summary>Compute the cell size for a target cell budget (Aspire's Standard ≈ 1M points).</summary>
    public static double CellSizeForBudget(HeightfieldData hf, int targetCells)
    {
        var (minX, minY, maxX, maxY) = hf.Bounds;
        double area = (maxX - minX) * (maxY - minY);
        return Math.Sqrt(area / Math.Max(1, targetCells));
    }
}
