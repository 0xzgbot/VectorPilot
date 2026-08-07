namespace VectorPilot.Geometry;

/// <summary>Axis-aligned bounding box in document units.</summary>
public readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly BoundingBox Empty = new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public VectorPoint Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public static BoundingBox FromPoints(IEnumerable<VectorPoint> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return minX > maxX ? Empty : new BoundingBox(minX, minY, maxX, maxY);
    }

    public BoundingBox Union(BoundingBox other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public bool Contains(VectorPoint p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
}
