using VectorPilot.Geometry;

namespace VectorPilot.Geometry;

/// <summary>
/// Batch shape transforms (ported from ShapeTransformer.swift, SPK-1101 family):
/// move / flip-horizontal / flip-vertical / scale / rotate about a center.
/// </summary>
public static class ShapeTransformer
{
    /// <summary>Centroid of the combined bounding box of all shapes
    /// (the Mac's boundingBoxCenter).</summary>
    public static VectorPoint BoundingBoxCenter(IReadOnlyList<VectorShape> shapes)
    {
        if (shapes.Count == 0) return new VectorPoint(0, 0);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var shape in shapes)
        {
            var b = BoundingBox.FromPoints(shape.Points);
            minX = Math.Min(minX, b.MinX); minY = Math.Min(minY, b.MinY);
            maxX = Math.Max(maxX, b.MaxX); maxY = Math.Max(maxY, b.MaxY);
        }
        return new VectorPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
    }

    public static List<VectorShape> Move(IEnumerable<VectorShape> shapes, double dx, double dy)
        => shapes.Select(s => Transform2D.TransformShape(s, p => Transform2D.Translate(p, dx, dy))).ToList();

    /// <summary>Mirror all shapes across the vertical line x = center.X (flip horizontal).</summary>
    public static List<VectorShape> FlipHorizontal(IEnumerable<VectorShape> shapes, VectorPoint center)
        => shapes.Select(s => Transform2D.FlipHorizontalShape(s, center.X)).ToList();

    /// <summary>Mirror all shapes across the horizontal line y = center.Y (flip vertical).</summary>
    public static List<VectorShape> FlipVertical(IEnumerable<VectorShape> shapes, VectorPoint center)
        => shapes.Select(s => Transform2D.FlipVerticalShape(s, center.Y)).ToList();

    /// <summary>Uniform scale about a center.</summary>
    public static List<VectorShape> Scale(IEnumerable<VectorShape> shapes, double factor, VectorPoint center)
        => shapes.Select(s => Transform2D.TransformShape(s, p => Transform2D.Scale(p, center, factor, factor))).ToList();

    /// <summary>Rotate all shapes about a center (degrees).</summary>
    public static List<VectorShape> Rotate(IEnumerable<VectorShape> shapes, double angleDegrees, VectorPoint center)
        => shapes.Select(s => Transform2D.TransformShape(s, p => Transform2D.Rotate(p, center, angleDegrees))).ToList();
}
