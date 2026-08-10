using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Fit-curves params (ported from FitCurvesParams.swift, D13).</summary>
public sealed class FitCurvesParams
{
    /// 0..1 — 0 keeps the polyline as-is, 1 maximal smoothing.
    public double Smoothing { get; set; } = 0.5;
    /// Direction changes sharper than this (degrees) stay hard corners.
    public double CornerAngleDegrees { get; set; } = 60;
    /// If > 0, segments longer than this (mm) are subdivided before fitting.
    public double MaxSegmentLengthMm { get; set; }
}

public sealed class FitCurvesResult
{
    public int InputPointCount { get; init; }
    public int OutputPointCount { get; init; }
    public int CornerCount { get; init; }
    public List<VectorPoint> Fitted { get; init; } = new();
}

/// <summary>
/// Fits smooth curves through polylines while preserving sharp corners
/// (ported from FitCurvesEngine.swift, SPK-PARITYWAVE1 D13).
/// </summary>
public static class FitCurvesEngine
{
    public static FitCurvesResult Fit(VectorShape shape, FitCurvesParams p)
    {
        var polyline = SamplePolyline(shape);

        if (p.MaxSegmentLengthMm > 0)
        {
            polyline = Resample(polyline, p.MaxSegmentLengthMm);
        }

        int inputCount = polyline.Count;
        if (inputCount < 3)
        {
            return new FitCurvesResult { InputPointCount = inputCount, OutputPointCount = inputCount, CornerCount = 0, Fitted = polyline };
        }

        double threshold = p.CornerAngleDegrees * Math.PI / 180.0;
        var corners = FindCorners(polyline, threshold);

        if (corners.Count == 0 && IsAllStraight(polyline))
        {
            return new FitCurvesResult { InputPointCount = inputCount, OutputPointCount = inputCount, CornerCount = 0, Fitted = polyline };
        }

        double smoothing = Math.Clamp(p.Smoothing, 0, 1);
        var fitted = polyline;
        if (smoothing > 0)
        {
            int window = 1 + (int)(smoothing * 4); // 1..5
            int iterations = smoothing >= 0.75 ? 2 : 1;
            fitted = Smooth(polyline, window, iterations, corners);
        }

        return new FitCurvesResult { InputPointCount = inputCount, OutputPointCount = fitted.Count, CornerCount = corners.Count, Fitted = fitted };
    }

    private static List<VectorPoint> SamplePolyline(VectorShape shape)
    {
        if (shape.Type == ShapeType.Polyline || shape.Type == ShapeType.Line)
        {
            return shape.Points.ToList();
        }
        var b = shape.Bounds();
        switch (shape.Type)
        {
            case ShapeType.Rectangle:
                return new List<VectorPoint>
                {
                    new(b.MinX, b.MinY), new(b.MaxX, b.MinY), new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY)
                };
            default:
                // Circles/ellipses and other closed shapes → 64-point ellipse sample.
                double rx = b.Width / 2.0, ry = b.Height / 2.0;
                var center = new VectorPoint((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
                return SampleClosedEllipse(center, rx, ry, 0, 64);
        }
    }

    private static List<VectorPoint> SampleClosedEllipse(VectorPoint center, double rx, double ry, double rotation, int count)
    {
        var points = new List<VectorPoint>();
        if (count < 3) return points;
        double cosR = Math.Cos(rotation), sinR = Math.Sin(rotation);
        for (int i = 0; i < count - 1; i++)
        {
            double angle = 2.0 * Math.PI * i / (count - 1);
            double x = rx * Math.Cos(angle);
            double y = ry * Math.Sin(angle);
            points.Add(new VectorPoint(center.X + x * cosR - y * sinR, center.Y + x * sinR + y * cosR));
        }
        points.Add(points[0]);
        return points;
    }

    private static List<VectorPoint> Resample(List<VectorPoint> points, double maxSegmentLengthMm)
    {
        if (points.Count < 2 || maxSegmentLengthMm <= 0) return points;
        var out_ = new List<VectorPoint> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= maxSegmentLengthMm)
            {
                out_.Add(b);
                continue;
            }
            int parts = Math.Min(10_000, Math.Max(1, (int)Math.Ceiling(length / maxSegmentLengthMm)));
            for (int k = 1; k <= parts; k++)
            {
                double t = (double)k / parts;
                out_.Add(new VectorPoint(a.X + dx * t, a.Y + dy * t));
            }
        }
        return out_;
    }

    private static List<int> FindCorners(List<VectorPoint> points, double thresholdRadians)
    {
        var corners = new List<int>();
        if (points.Count < 3) return corners;
        for (int i = 1; i < points.Count - 1; i++)
        {
            double ax = points[i].X - points[i - 1].X, ay = points[i].Y - points[i - 1].Y;
            double bx = points[i + 1].X - points[i].X, by = points[i + 1].Y - points[i].Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la <= 1e-12 || lb <= 1e-12) continue;
            double dot = (ax * bx + ay * by) / (la * lb);
            double angle = Math.Acos(Math.Clamp(dot, -1, 1));
            if (angle > thresholdRadians) corners.Add(i);
        }
        return corners;
    }

    private static bool IsAllStraight(List<VectorPoint> points)
    {
        for (int i = 1; i < points.Count - 1; i++)
        {
            double ax = points[i].X - points[i - 1].X, ay = points[i].Y - points[i - 1].Y;
            double bx = points[i + 1].X - points[i].X, by = points[i + 1].Y - points[i].Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la <= 1e-12 || lb <= 1e-12) continue;
            double dot = (ax * bx + ay * by) / (la * lb);
            if (dot < 1 - 1e-9) return false;
        }
        return true;
    }

    private static List<VectorPoint> Smooth(List<VectorPoint> points, int window, int iterations, List<int> corners)
    {
        if (points.Count < 3 || window < 2 || iterations < 1) return points;
        var cornerSet = corners.ToHashSet();
        int half = Math.Max(1, (window - 1) / 2);
        var current = points;
        for (int _ = 0; _ < iterations; _++)
        {
            var next = current.ToList();
            for (int i = 1; i < current.Count - 1; i++)
            {
                if (cornerSet.Contains(i)) continue;
                if (IsStraightRun(current, i)) continue;
                int lo = Math.Max(0, i - half);
                int hi = Math.Min(current.Count - 1, i + half);
                double sumX = 0, sumY = 0;
                int count = 0;
                for (int j = lo; j <= hi; j++)
                {
                    sumX += current[j].X;
                    sumY += current[j].Y;
                    count++;
                }
                next[i] = new VectorPoint(sumX / count, sumY / count);
            }
            current = next;
        }
        return current;
    }

    private static bool IsStraightRun(List<VectorPoint> points, int i)
    {
        double ax = points[i].X - points[i - 1].X, ay = points[i].Y - points[i - 1].Y;
        double bx = points[i + 1].X - points[i].X, by = points[i + 1].Y - points[i].Y;
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la <= 1e-12 || lb <= 1e-12) return true;
        double dot = (ax * bx + ay * by) / (la * lb);
        return dot >= 1 - 1e-9;
    }
}
