namespace VectorPilot.Engine;

/// <summary>Relief shape type (ported from ShapeType in ShapeReliefGenerator.swift, SPK-0703).
/// Named ReliefShapeType to avoid collision with VectorPilot.Geometry.ShapeType.</summary>
public enum ReliefShapeType
{
    Flat, Angled, Round, Smooth, Custom
}

/// <summary>Parameters for shape relief generation (ported from ShapeParameters).</summary>
public sealed class ReliefShapeParameters
{
    public double Angle { get; set; } = 45.0;
    public double Radius { get; set; } = 2.0;
    public double Smoothness { get; set; } = 0.5;
    public double FlatHeight { get; set; } = 0.0;

    public ReliefShapeParameters() { }

    public ReliefShapeParameters(double angle, double radius, double smoothness, double flatHeight)
    {
        Angle = angle;
        Radius = Math.Max(0, radius);
        Smoothness = Math.Clamp(smoothness, 0.0, 1.0);
        FlatHeight = flatHeight;
    }
}

/// <summary>
/// Parametric 3D relief generator (ported from ShapeReliefGenerator.swift, SPK-0703):
/// generates a heightfield from a ReliefShapeType + parameters. Output grids are
/// normalized to a requested footprint and max height so a generated shape drops
/// into the component stack and composites with everything else.
/// </summary>
public static class ShapeReliefGenerator
{
    /// <summary>Generate a shape relief. width/height are the WORLD footprint in mm;
    /// cellSizeMm sets the grid resolution; maxHeight is the peak for raised shapes.</summary>
    public static HeightfieldData Generate(
        ReliefShapeType shapeType,
        ReliefShapeParameters? parameters,
        double width,
        double height,
        double cellSizeMm = 1.0,
        double maxHeight = 10.0)
    {
        var p = parameters ?? new ReliefShapeParameters();
        int cols = Math.Max(2, (int)Math.Round(width / cellSizeMm));
        int rows = Math.Max(2, (int)Math.Round(height / cellSizeMm));
        double peak = Math.Max(0, maxHeight);
        var heights = new double[cols * rows];

        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                // Normalized cell center in [0,1] across the footprint.
                double nx = (i + 0.5) / cols;
                double ny = (j + 0.5) / rows;
                // Distance from footprint center, normalized to [0,1] at the farthest corner.
                double dx = (nx - 0.5) * 2.0;
                double dy = (ny - 0.5) * 2.0;
                double r = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(2.0));

                double h = shapeType switch
                {
                    ReliefShapeType.Flat => Math.Min(peak, Math.Max(0, p.FlatHeight)),
                    ReliefShapeType.Angled => nx * peak, // linear ramp along X
                    ReliefShapeType.Round => peak * Math.Sqrt(Math.Max(0, 1 - r * r)), // dome
                    ReliefShapeType.Smooth => Bell(p, peak, r), // cosine bell
                    ReliefShapeType.Custom => Math.Min(peak, Math.Max(0, p.FlatHeight)),
                    _ => 0
                };
                heights[j * cols + i] = Math.Max(0, h);
            }
        }
        return new HeightfieldData(cols, rows, cellSizeMm, 0, 0, heights);
    }

    private static double Bell(ReliefShapeParameters p, double peak, double r)
    {
        double spread = 0.35 + p.Smoothness * 0.55;
        double t = r / spread;
        if (t >= 1) return 0;
        return peak * 0.5 * (1 + Math.Cos(Math.PI * t));
    }
}