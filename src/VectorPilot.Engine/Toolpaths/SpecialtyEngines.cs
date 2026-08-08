using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

// SpecialtyResult is defined in RotaryWrapEngine.cs (shared result shape).

/// <summary>Boundary helpers shared by the specialty engines (ported from SpecialtyBoundary.swift).</summary>
public static class SpecialtyBoundary
{
    /// <summary>Closed polygon points for a path; degenerate paths (&lt; 3 points) → null.</summary>
    public static List<VectorPoint>? PolygonPoints(VectorShape path)
    {
        if (path.Points.Count < 3) return null;
        if (path.Closed && path.Points[0] == path.Points[^1]) return path.Points.ToList();
        var pts = path.Points.ToList();
        pts.Add(path.Points[0]);
        return pts;
    }

    /// <summary>Inside runs [x0, x1] of the horizontal line y within the polygon
    /// (even-odd rule, half-open edge test). Sorted left to right.</summary>
    public static List<(double X0, double X1)> InsideRuns(IReadOnlyList<VectorPoint> polygon, double y)
    {
        var runs = new List<(double, double)>();
        if (polygon.Count < 3) return runs;
        var crossings = new List<double>();
        int n = polygon.Count - 1;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[i + 1];
            double ay = a.Y, by = b.Y;
            if (Math.Abs(ay - by) < 1e-12) continue; // horizontal edge
            if ((ay <= y && by > y) || (by <= y && ay > y))
            {
                double t = (y - ay) / (by - ay);
                crossings.Add(a.X + t * (b.X - a.X));
            }
        }
        crossings.Sort();
        int idx = 0;
        while (idx + 1 < crossings.Count)
        {
            double x0 = crossings[idx], x1 = crossings[idx + 1];
            if (x1 - x0 > 1e-9) runs.Add((x0, x1));
            idx += 2;
        }
        return runs;
    }
}

// ---------------------------------------------------------------------------
// Prism (F10) — parallel V-grooves across closed vectors
// ---------------------------------------------------------------------------

public sealed class PrismToolpathParams
{
    public double SpacingMm { get; set; } = 6.0;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double MaxDepthMm { get; set; }     // 0 = uncapped
    public double StartDepthMm { get; set; }
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

public static class PrismToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, PrismToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=PRISM_TOOLPATH" };
        gcode.Add($"(V-Bit: {(int)p.VBitAngleDegrees}° · spacing {p.SpacingMm:0.00}mm)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        double tanHalf = Math.Tan(p.VBitAngleDegrees / 2 * Math.PI / 180);
        int grooveCount = 0;
        double totalLength = 0;

        foreach (var path in paths)
        {
            if (SpecialtyBoundary.PolygonPoints(path) is not { } poly) continue;
            double minY = poly.Min(pt => pt.Y), maxY = poly.Max(pt => pt.Y);
            double y = minY + p.SpacingMm / 2;
            while (y < maxY)
            {
                foreach (var run in SpecialtyBoundary.InsideRuns(poly, y))
                {
                    double width = run.X1 - run.X0;
                    double depth = Math.Min(width, p.SpacingMm) / (2 * Math.Max(tanHalf, 1e-9));
                    if (p.MaxDepthMm > 0) depth = Math.Min(depth, p.MaxDepthMm);
                    double z = -(p.StartDepthMm + depth);
                    grooveCount++;
                    totalLength += width;
                    gcode.Add("");
                    gcode.Add($"(Groove {grooveCount}: y {y:0.000} x {run.X0:0.000}–{run.X1:0.000})");
                    gcode.Add($"G0 X{run.X0:0.000} Y{y:0.000}");
                    gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                    gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
                    gcode.Add($"G1 X{run.X1:0.000} F{(int)p.FeedRateMmPerMin}");
                    gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                }
                y += p.SpacingMm;
            }
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + grooveCount * 1.5;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = grooveCount };
    }
}

// ---------------------------------------------------------------------------
// Fluting (F08) — flute along the selected vectors in step-down passes
// ---------------------------------------------------------------------------

public sealed class FlutingToolpathParams
{
    public double StartDepthMm { get; set; }
    public double CutDepthMm { get; set; } = 4.0;
    public double PassDepthMm { get; set; } = 2.0; // 0 = single pass
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double ToolDiameterMm { get; set; } = 6.0;
    public double SpindleRpm { get; set; }
}

public static class FlutingToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, FlutingToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=FLUTING_TOOLPATH" };
        gcode.Add($"(Tool: {(int)(p.ToolDiameterMm * 10)}mm · depth {p.CutDepthMm:0.00}mm)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        int passes = p.PassDepthMm > 0 ? Math.Max(1, (int)Math.Ceiling(p.CutDepthMm / p.PassDepthMm)) : 1;
        int fluteCount = 0;
        double totalLength = 0;

        foreach (var path in paths)
        {
            if (path.Points.Count < 2) continue;
            fluteCount++;
            for (int pass = 1; pass <= passes; pass++)
            {
                double depth = p.PassDepthMm > 0 ? Math.Min(pass * p.PassDepthMm, p.CutDepthMm) : p.CutDepthMm;
                double z = -(p.StartDepthMm + depth);
                var first = path.Points[0];
                gcode.Add("");
                gcode.Add($"(Flute {fluteCount} pass {pass}/{passes})");
                gcode.Add($"G0 X{first.X:0.000} Y{first.Y:0.000}");
                gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
                foreach (var pt in path.Points.Skip(1))
                {
                    gcode.Add($"G1 X{pt.X:0.000} Y{pt.Y:0.000} F{(int)p.FeedRateMmPerMin}");
                }
                gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                totalLength += PathLength(path.Points);
            }
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + fluteCount * passes * 1.2;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = fluteCount };
    }

    public static double PathLength(IReadOnlyList<VectorPoint> points)
    {
        double len = 0;
        for (int i = 1; i < points.Count; i++) len += points[i - 1].DistanceTo(points[i]);
        return len;
    }
}

// ---------------------------------------------------------------------------
// Chamfer (F11) — V-bit bevel ON the vector at chamfer-derived depth
// ---------------------------------------------------------------------------

public sealed class ChamferToolpathParams
{
    public double ChamferWidthMm { get; set; } = 3.0;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

public static class ChamferToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, ChamferToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=CHAMFER_TOOLPATH" };
        gcode.Add($"(V-Bit: {(int)p.VBitAngleDegrees}° · chamfer {p.ChamferWidthMm:0.00}mm)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        double tanHalf = Math.Tan(p.VBitAngleDegrees / 2 * Math.PI / 180);
        double z = -(p.ChamferWidthMm / Math.Max(tanHalf, 1e-9));
        int edgeCount = 0;
        double totalLength = 0;

        foreach (var path in paths)
        {
            if (path.Points.Count < 2) continue;
            edgeCount++;
            var first = path.Points[0];
            gcode.Add("");
            gcode.Add($"(Edge {edgeCount})");
            gcode.Add($"G0 X{first.X:0.000} Y{first.Y:0.000}");
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
            foreach (var pt in path.Points.Skip(1))
            {
                gcode.Add($"G1 X{pt.X:0.000} Y{pt.Y:0.000} F{(int)p.FeedRateMmPerMin}");
            }
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            totalLength += FlutingToolpathEngine.PathLength(path.Points);
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + edgeCount * 1.2;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = edgeCount };
    }
}

// ---------------------------------------------------------------------------
// Bevel Carving — interior bevel around a closed vector (composed over Chamfer:
// cuts ON the vector at the depth a bevelWidth V-cut needs)
// ---------------------------------------------------------------------------

public sealed class BevelCarvingParams
{
    public double BevelWidthMm { get; set; } = 3.0;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

public static class BevelCarvingEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, BevelCarvingParams p)
        => ChamferToolpathEngine.Compute(paths, new ChamferToolpathParams
        {
            ChamferWidthMm = p.BevelWidthMm,
            VBitAngleDegrees = p.VBitAngleDegrees,
            SafeZHeightMm = p.SafeZHeightMm,
            FeedRateMmPerMin = p.FeedRateMmPerMin,
            PlungeRateMmPerMin = p.PlungeRateMmPerMin,
            SpindleRpm = p.SpindleRpm
        });
}

// ---------------------------------------------------------------------------
// Quick Engrave (F07, SpecialtyResult flavor — the QuickEngraveEngine.swift
// port with per-vector depths also exists)
// ---------------------------------------------------------------------------

public sealed class QuickEngraveToolpathParams
{
    public double CutDepthMm { get; set; } = 1.0;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double ToolDiameterMm { get; set; } = 3.0;
    public double SpindleRpm { get; set; }
}

public static class QuickEngraveToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, QuickEngraveToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=QUICK_ENGRAVE_TOOLPATH" };
        gcode.Add($"(V-Bit: {(int)p.VBitAngleDegrees}° · depth {p.CutDepthMm:0.00}mm)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        double z = -p.CutDepthMm;
        int featureCount = 0;
        double totalLength = 0;

        foreach (var path in paths)
        {
            if (path.Points.Count < 2) continue;
            featureCount++;
            var first = path.Points[0];
            gcode.Add("");
            gcode.Add($"(Engrave {featureCount})");
            gcode.Add($"G0 X{first.X:0.000} Y{first.Y:0.000}");
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
            foreach (var pt in path.Points.Skip(1))
            {
                gcode.Add($"G1 X{pt.X:0.000} Y{pt.Y:0.000} F{(int)p.FeedRateMmPerMin}");
            }
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            totalLength += FlutingToolpathEngine.PathLength(path.Points);
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + featureCount * 1.2;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = featureCount };
    }
}

// ---------------------------------------------------------------------------
// Inlay toolpath (F15) — pocket (female V-carve with flat floor) / plug (male
// profile-on cut); recipes map to real params
// ---------------------------------------------------------------------------

public sealed class InlayToolpathParams
{
    public enum Variant { Pocket, Plug }
    public Variant VariantKind { get; set; } = Variant.Pocket;
    public double InlayDepthMm { get; set; } = 6.0;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1200;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double ToolDiameterMm { get; set; } = 6.0;
    public double SpindleRpm { get; set; }
}

/// <summary>Named V-carve inlay recipe (ported from VCarveInlayRecipe.swift).</summary>
public sealed class VCarveInlayRecipe
{
    public string Name { get; init; } = "";
    public double VBitAngleDegrees { get; init; } = 90.0;
    public double InlayDepthMm { get; init; } = 6.0;
    public double FeedRateMmPerMin { get; init; } = 1200;
    public double PlungeRateMmPerMin { get; init; } = 300;
    public double ToolDiameterMm { get; init; } = 6.0;

    public static readonly List<VCarveInlayRecipe> Presets = new()
    {
        new VCarveInlayRecipe { Name = "Fine 30° Inlay", VBitAngleDegrees = 30, InlayDepthMm = 2.5, FeedRateMmPerMin = 600, PlungeRateMmPerMin = 150, ToolDiameterMm = 3.175 },
        new VCarveInlayRecipe { Name = "Medium 45° Inlay", VBitAngleDegrees = 45, InlayDepthMm = 3.0, FeedRateMmPerMin = 800, PlungeRateMmPerMin = 200, ToolDiameterMm = 3.175 },
        new VCarveInlayRecipe { Name = "Bold 60° Inlay", VBitAngleDegrees = 60, InlayDepthMm = 4.0, FeedRateMmPerMin = 1000, PlungeRateMmPerMin = 300, ToolDiameterMm = 6.35 },
        new VCarveInlayRecipe { Name = "Deep 90° Inlay", VBitAngleDegrees = 90, InlayDepthMm = 5.0, FeedRateMmPerMin = 1200, PlungeRateMmPerMin = 400, ToolDiameterMm = 6.35 }
    };

    public static VCarveInlayRecipe? PresetNamed(string name) => Presets.FirstOrDefault(r => r.Name == name);

    public InlayToolpathParams ToParams(InlayToolpathParams.Variant variant) => new()
    {
        VariantKind = variant,
        VBitAngleDegrees = VBitAngleDegrees,
        InlayDepthMm = InlayDepthMm,
        FeedRateMmPerMin = FeedRateMmPerMin,
        PlungeRateMmPerMin = PlungeRateMmPerMin,
        ToolDiameterMm = ToolDiameterMm
    };
}

public static class InlayToolpathEngine
{
    /// <summary>Female half: flat-bottom V-carve of the shape interior.</summary>
    public static SpecialtyResult ComputePocket(IReadOnlyList<VectorShape> paths, InlayToolpathParams p)
    {
        var vc = new VCarveParams
        {
            VBitAngleDegrees = p.VBitAngleDegrees,
            FeedRateMmPerMin = p.FeedRateMmPerMin,
            PlungeFeedRateMmPerMin = p.PlungeRateMmPerMin,
            MaxDepthOfCutMm = p.InlayDepthMm,
            FlatBottomMode = true,
            FlatDepthMm = p.InlayDepthMm
        };
        var result = VCarveEngine.Compute(paths, vc);
        return new SpecialtyResult { GcodeLines = result.GcodeLines, EstimatedTimeSeconds = result.EstimatedTimeSeconds, FeatureCount = paths.Count };
    }

    /// <summary>Male half: profile "on" cut at the inlay depth.</summary>
    public static SpecialtyResult ComputePlug(IReadOnlyList<VectorShape> paths, InlayToolpathParams p)
    {
        var pp = new ProfileToolpathParams
        {
            CutMode = ProfileCutMode.OnCut,
            FeedRateMmPerMin = p.FeedRateMmPerMin,
            PlungeFeedRateMmPerMin = p.PlungeRateMmPerMin,
            MaxDepthOfCutMm = p.InlayDepthMm,
            ToolDiameterMm = p.ToolDiameterMm,
            SpindleRpm = p.SpindleRpm
        };
        var result = ProfileToolpathEngine.Compute(paths, pp);
        return new SpecialtyResult { GcodeLines = result.GcodeLines, EstimatedTimeSeconds = result.EstimatedTimeSeconds, FeatureCount = paths.Count };
    }
}

// ---------------------------------------------------------------------------
// Drag knife (SPK-0907) — blade-offset + corner pivot toolpath
// ---------------------------------------------------------------------------

public sealed class DragKnifeToolpathParams
{
    public double BladeOffsetMm { get; set; } = 4.0;
    public double CutDepthMm { get; set; } = 2.0;
    public double PivotThresholdDegrees { get; set; } = 0.5;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1200;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

public static class DragKnifeToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, DragKnifeToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=DRAG_KNIFE_TOOLPATH" };
        gcode.Add($"(Drag knife: blade offset {p.BladeOffsetMm:0.00}mm · depth {p.CutDepthMm:0.00}mm)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        double b = Math.Max(0.05, p.BladeOffsetMm);
        double threshold = p.PivotThresholdDegrees * Math.PI / 180;
        double z = -p.CutDepthMm;
        int featureCount = 0, pivotCount = 0;
        double totalLength = 0;

        static (double X, double Y) Dir(IReadOnlyList<VectorPoint> pts, int i)
        {
            double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len > 1e-9 ? (dx / len, dy / len) : (1.0, 0.0);
        }

        foreach (var path in paths)
        {
            var pts = path.Points.ToList();
            if (pts.Count < 2) continue;
            bool closed = path.Closed;
            if (closed && pts[0] != pts[^1]) pts.Add(pts[0]);
            featureCount++;

            var u0 = Dir(pts, 0);
            double startX = pts[0].X + b * u0.X, startY = pts[0].Y + b * u0.Y;
            gcode.Add("");
            gcode.Add($"(Drag knife path {featureCount})");
            gcode.Add($"G0 X{startX:0.000} Y{startY:0.000}");
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");

            var prevU = u0;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var corner = pts[i];
                var nextU = Dir(pts, i);
                double sX = corner.X + b * prevU.X, sY = corner.Y + b * prevU.Y;
                double eX = corner.X + b * nextU.X, eY = corner.Y + b * nextU.Y;
                double cross = prevU.X * nextU.Y - prevU.Y * nextU.X;
                double turn = Math.Abs(cross);
                if (turn > Math.Sin(threshold) && turn > 1e-9)
                {
                    double iJ = corner.X - sX, jJ = corner.Y - sY;
                    string word = cross > 0 ? "G3" : "G2";
                    gcode.Add($"G1 X{sX:0.000} Y{sY:0.000} F{(int)p.FeedRateMmPerMin}");
                    gcode.Add($"(Pivot {pivotCount + 1}: {(int)(Math.Sign(cross) * Math.Round(Math.Asin(Math.Min(1.0, turn)) * 180 / Math.PI))}° at {corner.X:0.000},{corner.Y:0.000})");
                    gcode.Add($"{word} X{eX:0.000} Y{eY:0.000} I{iJ:0.000} J{jJ:0.000} F{(int)p.FeedRateMmPerMin}");
                    pivotCount++;
                    totalLength += b * Math.Abs(Math.Atan2(nextU.Y, nextU.X) - Math.Atan2(prevU.Y, prevU.X));
                }
                else
                {
                    gcode.Add($"G1 X{eX:0.000} Y{eY:0.000} F{(int)p.FeedRateMmPerMin}");
                }
                prevU = nextU;
            }

            var lastU = Dir(pts, pts.Count - 2);
            double endX = pts[^1].X + b * lastU.X, endY = pts[^1].Y + b * lastU.Y;
            gcode.Add($"G1 X{endX:0.000} Y{endY:0.000} F{(int)p.FeedRateMmPerMin}");

            if (closed && pts.Count > 2)
            {
                var corner = pts[^1];
                double sX = corner.X + b * lastU.X, sY = corner.Y + b * lastU.Y;
                double eX = corner.X + b * u0.X, eY = corner.Y + b * u0.Y;
                double cross = lastU.X * u0.Y - lastU.Y * u0.X;
                double turn = Math.Abs(cross);
                if (turn > Math.Sin(threshold) && turn > 1e-9)
                {
                    double iJ = corner.X - sX, jJ = corner.Y - sY;
                    string word = cross > 0 ? "G3" : "G2";
                    gcode.Add($"G1 X{sX:0.000} Y{sY:0.000} F{(int)p.FeedRateMmPerMin}");
                    gcode.Add($"(Pivot {pivotCount + 1}: {(int)(Math.Sign(cross) * Math.Round(Math.Asin(Math.Min(1.0, turn)) * 180 / Math.PI))}° at {corner.X:0.000},{corner.Y:0.000})");
                    gcode.Add($"{word} X{eX:0.000} Y{eY:0.000} I{iJ:0.000} J{jJ:0.000} F{(int)p.FeedRateMmPerMin}");
                    pivotCount++;
                }
                else
                {
                    gcode.Add($"G1 X{eX:0.000} Y{eY:0.000} F{(int)p.FeedRateMmPerMin}");
                }
            }
            gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
            totalLength += FlutingToolpathEngine.PathLength(pts);
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + (featureCount + pivotCount) * 1.2;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = featureCount };
    }
}

// ---------------------------------------------------------------------------
// Texture (SPK-0900) — parallel/crosshatch grooves clipped inside boundaries
// ---------------------------------------------------------------------------

public sealed class TextureToolpathParams
{
    public enum Pattern { Parallel, Crosshatch }
    public enum CutStyle { VGroove, Flat }

    public Pattern PatternKind { get; set; } = Pattern.Parallel;
    public double SpacingMm { get; set; } = 6.0;
    public double AngleDegrees { get; set; }
    public CutStyle Style { get; set; } = CutStyle.VGroove;
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double FlatDepthMm { get; set; } = 2.0;
    public double MaxDepthMm { get; set; }
    public double SafeZHeightMm { get; set; } = 5.0;
    public double FeedRateMmPerMin { get; set; } = 1500;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; }
}

public static class TextureToolpathEngine
{
    public static SpecialtyResult Compute(IReadOnlyList<VectorShape> paths, TextureToolpathParams p)
    {
        var gcode = new List<string> { "%", "O=TEXTURE_TOOLPATH" };
        gcode.Add($"(Texture: {(p.PatternKind == TextureToolpathParams.Pattern.Crosshatch ? "crosshatch" : "parallel")} · spacing {p.SpacingMm:0.00}mm · {(int)p.AngleDegrees}°)");
        if (p.SpindleRpm > 0) gcode.Add($"M3 S{(int)p.SpindleRpm}");
        double tanHalf = Math.Tan(p.VBitAngleDegrees / 2 * Math.PI / 180);
        double spacing = Math.Max(0.1, p.SpacingMm);
        int grooveCount = 0;
        double totalLength = 0;

        var angles = p.PatternKind == TextureToolpathParams.Pattern.Crosshatch
            ? new[] { p.AngleDegrees, p.AngleDegrees + 90 }
            : new[] { p.AngleDegrees };

        foreach (var path in paths)
        {
            if (SpecialtyBoundary.PolygonPoints(path) is not { } poly) continue;
            foreach (double angle in angles)
            {
                double rad = -angle * Math.PI / 180;
                double cosA = Math.Cos(rad), sinA = Math.Sin(rad);
                var rotated = poly.Select(pt => new VectorPoint(pt.X * cosA - pt.Y * sinA, pt.X * sinA + pt.Y * cosA)).ToList();
                double minY = rotated.Min(pt => pt.Y), maxY = rotated.Max(pt => pt.Y);
                double y = minY + spacing / 2;
                while (y < maxY)
                {
                    foreach (var run in SpecialtyBoundary.InsideRuns(rotated, y))
                    {
                        double width = run.X1 - run.X0;
                        double depth;
                        if (p.Style == TextureToolpathParams.CutStyle.VGroove)
                        {
                            double d = Math.Min(width, spacing) / (2 * Math.Max(tanHalf, 1e-9));
                            if (p.MaxDepthMm > 0) d = Math.Min(d, p.MaxDepthMm);
                            depth = d;
                        }
                        else
                        {
                            depth = p.FlatDepthMm;
                        }
                        double z = -depth;
                        double ax = run.X0 * cosA + y * sinA;
                        double ay = -run.X0 * sinA + y * cosA;
                        double bx = run.X1 * cosA + y * sinA;
                        double by = -run.X1 * sinA + y * cosA;
                        grooveCount++;
                        totalLength += width;
                        gcode.Add("");
                        gcode.Add($"(Texture groove {grooveCount})");
                        gcode.Add($"G0 X{ax:0.000} Y{ay:0.000}");
                        gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                        gcode.Add($"G1 Z{z:0.000} F{(int)p.PlungeRateMmPerMin}");
                        gcode.Add($"G1 X{bx:0.000} Y{by:0.000} F{(int)p.FeedRateMmPerMin}");
                        gcode.Add($"G0 Z{p.SafeZHeightMm:0.0}");
                    }
                    y += spacing;
                }
            }
        }
        gcode.Add("");
        gcode.Add("M30");
        gcode.Add("%");
        double time = totalLength / Math.Max(p.FeedRateMmPerMin, 1) * 60.0 + grooveCount * 1.5;
        return new SpecialtyResult { GcodeLines = gcode, EstimatedTimeSeconds = time, FeatureCount = grooveCount };
    }
}
