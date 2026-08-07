namespace VectorPilot.Geometry;

/// <summary>A 2D point in document units.</summary>
public readonly record struct VectorPoint(double X, double Y)
{
    public static readonly VectorPoint Zero = new(0, 0);

    public static VectorPoint operator +(VectorPoint a, VectorPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static VectorPoint operator -(VectorPoint a, VectorPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static VectorPoint operator *(VectorPoint a, double s) => new(a.X * s, a.Y * s);
    public double DistanceTo(VectorPoint other) => Math.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));
    public override string ToString() => $"({X:F3}, {Y:F3})";
}
