using VectorPilot.Geometry;

namespace VectorPilot.Engine;

// Sweep / extrude / weave estimators (ported from SweepExtrudeWeave.swift).
// SweepProfile is defined in SweepReliefEngine.cs.

/// <summary>Minimal 3D vector for extrusion direction.</summary>
public readonly record struct Vector3(double X, double Y, double Z);
public enum ExtrudeType { Linear, Bilateral }
public enum WeavePattern { Plain, Twill, Satin }

public sealed class SweepProfileParams
{
    public SweepProfile Profile { get; set; } = SweepProfile.Rectangle;
    public double Width { get; set; } = 10.0;
    public double Height { get; set; } = 5.0;
    public double Radius { get; set; } = 3.0;

    public double Area => Profile == SweepProfile.Circle ? Math.PI * Radius * Radius : Width * Height;
}

public sealed class TwoRailSweepParams
{
    public List<VectorPoint> Rail1Points { get; set; } = new();
    public List<VectorPoint> Rail2Points { get; set; } = new();
    public SweepProfileParams Profile { get; set; } = new();
}

public sealed class ExtrudeParams
{
    public Vector3 Direction { get; set; } = new(0, 0, 1);
    public double Distance { get; set; } = 10.0;
    public bool Bilateral { get; set; }
    public ExtrudeType Type { get; set; } = ExtrudeType.Linear;
}

public sealed class WeaveParams
{
    public WeavePattern Pattern { get; set; } = WeavePattern.Plain;
    public double ThreadSize { get; set; } = 1.0;
    public double Spacing { get; set; } = 2.0;
    public int WarpCount { get; set; } = 20;
    public int WeftCount { get; set; } = 20;
    public double Overlap { get; set; } = 0.5;
    public double Tension { get; set; } = 0.5;
}

public sealed class SweepExtrudeWeaveResult
{
    public string Operation { get; init; } = "";
    public Guid ComponentId { get; init; }
    public List<Guid> NewComponentIds { get; init; } = new();
    public double VolumeMm3 { get; init; }
    public double SurfaceAreaMm2 { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public static class SweepExtrudeWeaveEngine
{
    public static SweepExtrudeWeaveResult TwoRailSweep(Guid componentId, TwoRailSweepParams p)
    {
        if (p.Rail1Points.Count < 2) return Fail("twoRailSweep", componentId, "Rail 1 must have at least 2 points");
        if (p.Rail2Points.Count < 2) return Fail("twoRailSweep", componentId, "Rail 2 must have at least 2 points");
        if (p.Rail1Points.Count != p.Rail2Points.Count) return Fail("twoRailSweep", componentId, "Both rails must have the same number of points");

        double avgRailLength = AveragePathLength(p.Rail1Points);
        double profileArea = p.Profile.Area;
        double volume = avgRailLength * profileArea;
        double surfaceArea = avgRailLength * (p.Profile.Width + p.Profile.Height);

        return new SweepExtrudeWeaveResult
        {
            Operation = "twoRailSweep", ComponentId = componentId, NewComponentIds = new() { componentId },
            VolumeMm3 = volume, SurfaceAreaMm2 = surfaceArea, Success = true
        };
    }

    public static SweepExtrudeWeaveResult Extrude(Guid componentId, ExtrudeParams p, double bboxWidth, double bboxHeight)
    {
        double dirMag = Math.Sqrt(p.Direction.X * p.Direction.X + p.Direction.Y * p.Direction.Y + p.Direction.Z * p.Direction.Z);
        if (dirMag < 0.001) return Fail("extrude", componentId, "Extrusion direction must not be zero");

        double baseArea = bboxWidth * bboxHeight;
        double effectiveDistance = p.Bilateral ? p.Distance * 2 : p.Distance;
        double volume = baseArea * effectiveDistance;
        double perimeter = 2 * (bboxWidth + bboxHeight);
        double sideArea = perimeter * effectiveDistance;
        double surfaceArea = sideArea + baseArea * 2;

        return new SweepExtrudeWeaveResult
        {
            Operation = "extrude", ComponentId = componentId, NewComponentIds = new() { componentId },
            VolumeMm3 = volume, SurfaceAreaMm2 = surfaceArea, Success = true
        };
    }

    public static SweepExtrudeWeaveResult Weave(Guid componentId, WeaveParams p, double bboxWidth, double bboxHeight)
    {
        if (p.WarpCount <= 0 || p.WeftCount <= 0) return Fail("weave", componentId, "Warp and weft counts must be positive");

        double totalThreadLength = (p.WarpCount + p.WeftCount) * Math.Max(bboxWidth, bboxHeight);
        double threadCrossSection = p.ThreadSize * p.ThreadSize;
        double volume = totalThreadLength * threadCrossSection * p.Overlap;
        double surfaceArea = totalThreadLength * p.ThreadSize * 2;

        return new SweepExtrudeWeaveResult
        {
            Operation = "weave", ComponentId = componentId, NewComponentIds = new() { componentId },
            VolumeMm3 = volume, SurfaceAreaMm2 = surfaceArea, Success = true
        };
    }

    public static (bool IsValid, List<string> Errors) ValidateTwoRailSweep(TwoRailSweepParams p)
    {
        var errors = new List<string>();
        if (p.Rail1Points.Count < 2) errors.Add("Rail 1 needs at least 2 points");
        if (p.Rail2Points.Count < 2) errors.Add("Rail 2 needs at least 2 points");
        if (p.Rail1Points.Count != p.Rail2Points.Count) errors.Add("Rails must have equal points");
        return (errors.Count == 0, errors);
    }

    public static (bool IsValid, List<string> Errors) ValidateExtrude(ExtrudeParams p)
    {
        var errors = new List<string>();
        double dirMag = Math.Sqrt(p.Direction.X * p.Direction.X + p.Direction.Y * p.Direction.Y + p.Direction.Z * p.Direction.Z);
        if (dirMag < 0.001) errors.Add("Extrusion direction must not be zero");
        if (p.Distance <= 0) errors.Add("Distance must be positive");
        return (errors.Count == 0, errors);
    }

    public static (bool IsValid, List<string> Errors) ValidateWeave(WeaveParams p)
    {
        var errors = new List<string>();
        if (p.WarpCount <= 0 || p.WeftCount <= 0) errors.Add("Warp and weft counts must be positive");
        if (p.ThreadSize <= 0) errors.Add("Thread size must be positive");
        return (errors.Count == 0, errors);
    }

    private static SweepExtrudeWeaveResult Fail(string op, Guid id, string msg)
        => new() { Operation = op, ComponentId = id, Success = false, ErrorMessage = msg };

    private static double AveragePathLength(IReadOnlyList<VectorPoint> pts)
    {
        if (pts.Count < 2) return 0;
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
        return len;
    }
}
