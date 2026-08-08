using VectorPilot.Geometry;

namespace VectorPilot.Engine;

// ---------------------------------------------------------------------------
// Laser strategies (Aspire laser add-on parity; clean implementations on the
// existing engine pattern). Lasers cut with power (S) instead of Z depth.
// ---------------------------------------------------------------------------

public sealed class LaserCutParams
{
    public double PowerPercent { get; set; } = 80;
    public double SpeedMmPerMin { get; set; } = 1000;
    public double Passes { get; set; } = 1;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double SpindleRpm { get; set; } // lasers ignore; kept for parity
}

/// <summary>Laser cut: trace vectors with the laser on (M3 S&lt;power&gt;), off at rapids.</summary>
public static class LaserCutEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, LaserCutParams p)
    {
        var gcode = new List<string> { "%", "O=LASER_CUT_TOOLPATH" };
        gcode.Add($"(Laser Cut: {p.PowerPercent:0}% power · {p.SpeedMmPerMin:0} mm/min)");
        gcode.Add($"M3 S{(int)(p.PowerPercent * 10)}");
        double length = 0;
        int features = 0;
        for (int pass = 0; pass < Math.Max(1, (int)p.Passes); pass++)
        {
            foreach (var path in paths)
            {
                if (path.Points.Count < 2) continue;
                features++;
                gcode.Add("");
                gcode.Add($"(Laser pass {pass + 1}, feature {features})");
                gcode.Add($"G0 X{path.Points[0].X:0.000} Y{path.Points[0].Y:0.000}");
                for (int i = 1; i < path.Points.Count; i++)
                {
                    gcode.Add($"G1 X{path.Points[i].X:0.000} Y{path.Points[i].Y:0.000} F{(int)p.SpeedMmPerMin}");
                    length += path.Points[i - 1].DistanceTo(path.Points[i]);
                }
                if (path.Closed && path.Points.Count > 2)
                {
                    gcode.Add($"G1 X{path.Points[0].X:0.000} Y{path.Points[0].Y:0.000} F{(int)p.SpeedMmPerMin}");
                }
            }
        }
        gcode.Add("M5");
        gcode.Add("M30");
        gcode.Add("%");
        double time = length * Math.Max(1, (int)p.Passes) / Math.Max(1, p.SpeedMmPerMin) * 60.0;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = features };
    }
}

public sealed class LaserFillParams
{
    public double PowerPercent { get; set; } = 60;
    public double SpeedMmPerMin { get; set; } = 1500;
    public double LineSpacingMm { get; set; } = 0.5;
}

/// <summary>Laser fill: raster hatch inside the closed boundary (X-parallel lines).</summary>
public static class LaserFillEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, LaserFillParams p)
    {
        var gcode = new List<string> { "%", "O=LASER_FILL_TOOLPATH" };
        gcode.Add($"(Laser Fill: {p.PowerPercent:0}% power · {p.LineSpacingMm:0.00}mm lines)");
        gcode.Add($"M3 S{(int)(p.PowerPercent * 10)}");
        int lines = 0;
        double length = 0;
        foreach (var path in paths)
        {
            if (SpecialtyBoundary.PolygonPoints(path) is not { } poly) continue;
            double minY = poly.Min(pt => pt.Y), maxY = poly.Max(pt => pt.Y);
            double y = minY + p.LineSpacingMm / 2;
            while (y < maxY)
            {
                foreach (var run in SpecialtyBoundary.InsideRuns(poly, y))
                {
                    lines++;
                    length += run.X1 - run.X0;
                    gcode.Add($"G0 X{run.X0:0.000} Y{y:0.000}");
                    gcode.Add($"G1 X{run.X1:0.000} Y{y:0.000} F{(int)p.SpeedMmPerMin}");
                }
                y += p.LineSpacingMm;
            }
        }
        gcode.Add("M5");
        gcode.Add("M30");
        gcode.Add("%");
        double time = length / Math.Max(1, p.SpeedMmPerMin) * 60.0;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = lines };
    }
}

public sealed class LaserPictureParams
{
    public double PowerPercent { get; set; } = 80;
    public double SpeedMmPerMin { get; set; } = 1200;
    public double DotSpacingMm { get; set; } = 0.5;
}

/// <summary>Laser picture: grayscale dithering — dark pixels get higher power.</summary>
public static class LaserPictureEngine
{
    public static SpecialtyResult Compute(HeightfieldData heightfield, LaserPictureParams p)
    {
        var gcode = new List<string> { "%", "O=LASER_PICTURE_TOOLPATH" };
        gcode.Add($"(Laser Picture: {p.PowerPercent:0}% max power · {p.DotSpacingMm:0.00}mm dots)");
        double maxH = Math.Max(heightfield.MaxHeight, 1e-9);
        int dots = 0;
        double row = 0;
        int stride = Math.Max(1, (int)Math.Round(p.DotSpacingMm / heightfield.CellSizeMm));
        while (row < heightfield.Height)
        {
            double cy = heightfield.MinY + (row + 0.5) * heightfield.CellSizeMm;
            int col = 0;
            while (col < heightfield.Width)
            {
                double cx = heightfield.MinX + (col + 0.5) * heightfield.CellSizeMm;
                double lum = Math.Clamp(heightfield.HeightInterpolated(cx, cy) / maxH, 0, 1);
                int power = (int)((1.0 - lum) * p.PowerPercent * 10);
                if (power > 5)
                {
                    dots++;
                    gcode.Add($"G0 X{cx:0.000} Y{cy:0.000}");
                    gcode.Add($"M3 S{power}");
                    gcode.Add($"G4 P0.001");
                }
                col += stride;
            }
            row += stride;
        }
        gcode.Add("M5");
        gcode.Add("M30");
        gcode.Add("%");
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = dots * 0.02, FeatureCount = dots };
    }
}
