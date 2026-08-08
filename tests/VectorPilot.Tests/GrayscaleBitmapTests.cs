using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class GrayscaleBitmapTests
{
    private static HeightfieldData Ramp()
    {
        var h = new double[25];
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
                h[j * 5 + i] = j; // rows 0..4
        return new HeightfieldData(5, 5, 1.0, 0, 0, h);
    }

    [Fact]
    public void Bmp_RoundTrips_Through_Gray()
    {
        var hf = Ramp();
        var bmp = GrayscaleBitmap.ToBmp(hf);
        Assert.True(bmp.Length > 54);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);

        // Read back the pixel bytes (54-byte headers + 1024-byte palette, bottom-up rows).
        const int pixelOffset = 14 + 40 + 1024;
        int w = 5, rowSize = 8; // (5+3)&~3
        var gray = new byte[w * 5];
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < w; x++)
            {
                gray[y * w + x] = bmp[pixelOffset + (4 - y) * rowSize + x];
            }
        }
        var back = GrayscaleBitmap.FromGray(gray, 5, 5, 1.0, 0, 0, maxHeight: 4);
        Assert.Equal(0.0, back.HeightAt(0.5, 0.5)!.Value, 1);
        Assert.Equal(4.0, back.HeightAt(0.5, 4.5)!.Value, 1);
    }

    [Fact]
    public void Png_Has_Valid_Signature_And_Chunks()
    {
        var hf = Ramp();
        var png = GrayscaleBitmap.ToPng(hf);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
        var header = System.Text.Encoding.ASCII.GetString(png, 12, 4);
        Assert.Equal("IHDR", header);
        Assert.Contains("IDAT", System.Text.Encoding.ASCII.GetString(png));
        Assert.Contains("IEND", System.Text.Encoding.ASCII.GetString(png));
    }

    [Fact]
    public void FromGray_Scales_To_MaxHeight()
    {
        var gray = new byte[] { 0, 128, 255 };
        var hf = GrayscaleBitmap.FromGray(gray, 3, 1, 1.0, 0, 0, maxHeight: 10);
        Assert.Equal(0.0, hf.Heights[0], 3);
        Assert.Equal(10.0 * 128 / 255.0, hf.Heights[1], 3);
        Assert.Equal(10.0, hf.Heights[2], 3);
    }
}

public class HeightfieldMathTests
{
    [Fact]
    public void Resample_Coarser_Keeps_Shape()
    {
        var h = new double[64];
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                h[j * 8 + i] = i;
        var hf = new HeightfieldData(8, 8, 1.0, 0, 0, h);

        var coarse = HeightfieldMath.Resample(hf, 2.0);
        Assert.NotNull(coarse);
        Assert.Equal(4, coarse!.Width);
        Assert.Equal(4, coarse.Height);
        Assert.Equal(2.0, coarse.CellSizeMm, 6);
        Assert.True(coarse.MaxHeight <= 7.0 + 1e-9);
    }

    [Fact]
    public void Resample_Finer_Interpolates()
    {
        var h = new double[4];
        Array.Fill(h, 4.0);
        var hf = new HeightfieldData(2, 2, 2.0, 0, 0, h);
        var fine = HeightfieldMath.Resample(hf, 1.0);
        Assert.NotNull(fine);
        Assert.Equal(4, fine!.Width);
        Assert.Equal(4.0, fine.MaxHeight, 6);
    }

    [Fact]
    public void CellSizeForBudget_Scales()
    {
        var hf = new HeightfieldData(10, 10, 1.0, 0, 0, new double[100]);
        double cell = HeightfieldMath.CellSizeForBudget(hf, 100);
        Assert.Equal(1.0, cell, 6); // 10x10 area / 100 cells
        double coarse = HeightfieldMath.CellSizeForBudget(hf, 25);
        Assert.Equal(2.0, coarse, 6);
    }
}
