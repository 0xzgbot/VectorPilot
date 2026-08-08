using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class BitmapTracerTests
{
    private static byte[] Img(int width, int height, Func<int, int, bool> inside, byte on = 255)
    {
        var px = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                px[y * width + x] = inside(x, y) ? on : (byte)0;
        return px;
    }

    [Fact]
    public void Solid_Block_Yields_One_Closed_Contour_With_Expected_Area()
    {
        // 8x8 image, solid 4x4 block at pixels (2..5, 2..5).
        var px = Img(8, 8, (x, y) => x >= 2 && x <= 5 && y >= 2 && y <= 5);
        var contours = BitmapTracer.Trace(px, 8, 8, threshold: 128);

        Assert.Single(contours);
        var c = contours[0];
        Assert.True(c.Closed);
        // Contour runs through edge midpoints: block (2..6) squared.
        var b = c.Bounds();
        Assert.Equal(2.5, b.MinX, 3);
        Assert.Equal(2.5, b.MinY, 3);
        Assert.Equal(6.5, b.MaxX, 3);
        Assert.Equal(6.5, b.MaxY, 3);
        // Marching squares chamfers pixel corners: 16 minus 4 corner cuts of 0.125.
        Assert.Equal(15.5, Math.Abs(GeometryMath.SignedArea(c.Points)), 2);
    }

    [Fact]
    public void Empty_Image_Yields_No_Contours()
    {
        var px = Img(8, 8, (_, _) => false);
        Assert.Empty(BitmapTracer.Trace(px, 8, 8));
    }

    [Fact]
    public void Full_Image_Yields_One_Border_Contour()
    {
        var px = Img(8, 8, (_, _) => true);
        var contours = BitmapTracer.Trace(px, 8, 8);
        Assert.Single(contours);
        var b = contours[0].Bounds();
        Assert.Equal(0.5, b.MinX, 3);
        Assert.Equal(8.5, b.MaxX, 3);
    }

    [Fact]
    public void Donut_Yields_Two_Contours()
    {
        // 12x12, ring: outside of a 2x2 hole at the center.
        var px = Img(12, 12, (x, y) => !(x >= 5 && x <= 6 && y >= 5 && y <= 6));
        var contours = BitmapTracer.Trace(px, 12, 12);
        Assert.Equal(2, contours.Count);
        // One contour bounds the outer edge, one wraps the hole (opposite winding).
        var areas = contours.Select(c => GeometryMath.SignedArea(c.Points)).OrderBy(a => a).ToList();
        Assert.True(areas[0] < 0 && areas[1] > 0);
    }

    [Fact]
    public void Threshold_Filters_Gray_Values()
    {
        // A 2x2 block at 200; the rest at 100 (both below/above the 128 threshold).
        var px = Img(8, 8, (x, y) => x >= 3 && x <= 4 && y >= 3 && y <= 4, on: 200);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if (!(x >= 3 && x <= 4 && y >= 3 && y <= 4)) px[y * 8 + x] = 100;

        var contours = BitmapTracer.Trace(px, 8, 8, threshold: 128);
        Assert.Single(contours);
        var b = contours[0].Bounds();
        Assert.Equal(3.5, b.MinX, 3);
        Assert.Equal(5.5, b.MaxX, 3);
        Assert.Equal(5.5, b.MaxY, 3);
    }

    [Fact]
    public void Single_Pixel_Yields_Small_Diamond()
    {
        var px = Img(4, 4, (x, y) => x == 2 && y == 2);
        var contours = BitmapTracer.Trace(px, 4, 4);
        Assert.Single(contours);
        // Unit diamond (diagonals 1x1) — marching-squares corner cut.
        Assert.Equal(0.5, Math.Abs(GeometryMath.SignedArea(contours[0].Points)), 2);
    }
}
