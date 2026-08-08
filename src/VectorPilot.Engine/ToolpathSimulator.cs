using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>2D grid of material heights for simulation (ported from Heightmap.swift).</summary>
public sealed class Heightmap
{
    public int Width { get; }
    public int Height { get; }
    public double CellSizeMm { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double[] Data { get; }

    public Heightmap(int width, int height, double cellSizeMm, double minX, double minY, double initialHeight = 0)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        CellSizeMm = cellSizeMm;
        MinX = minX;
        MinY = minY;
        Data = new double[Width * Height];
        Array.Fill(Data, initialHeight);
    }

    public double GetHeight(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
        return Data[y * Width + x];
    }

    public void SetHeight(double value, int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        Data[y * Width + x] = value;
    }

    public (double X, double Y) WorldPosition(int x, int y) => (MinX + x * CellSizeMm, MinY + y * CellSizeMm);

    public (int X, int Y) GridPosition(double wx, double wy)
        => ((int)((wx - MinX) / CellSizeMm), (int)((wy - MinY) / CellSizeMm));

    public (double MinX, double MinY, double MaxX, double MaxY) Bounds
        => (MinX, MinY, MinX + (Width - 1) * CellSizeMm, MinY + (Height - 1) * CellSizeMm);
}

public sealed class SimulationResult
{
    public Heightmap FinalHeightmap { get; init; } = null!;
    public double SimulationTimeSeconds { get; init; }
    public bool IsCancelled { get; init; }
    public bool Success { get; init; } = true;
    public double MaxRemovalMm => 0; // approximation — would need initial vs final comparison
}

/// <summary>Simulates toolpath execution on a heightmap (ported from ToolpathSimulator.swift).</summary>
public sealed class ToolpathSimulator
{
    private readonly Heightmap _initial;

    public double StockTopHeight => _initial.Data.Length == 0 ? 0 : _initial.Data[0];

    public ToolpathSimulator(Heightmap initialHeightmap) => _initial = initialHeightmap;

    public static ToolpathSimulator CreateDefault(double cellSizeMm = 0.5, double stockWidthMm = 100, double stockHeightMm = 100)
    {
        int w = (int)(stockWidthMm / cellSizeMm);
        int h = (int)(stockHeightMm / cellSizeMm);
        return new ToolpathSimulator(new Heightmap(w, h, cellSizeMm, 0, 0, stockHeightMm));
    }

    public SimulationResult Simulate(IReadOnlyList<string> toolpathGcode, Func<bool>? shouldCancel = null)
    {
        shouldCancel ??= () => false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var working = new Heightmap(_initial.Width, _initial.Height, _initial.CellSizeMm, _initial.MinX, _initial.MinY)
        {
            // copy data
        };
        Array.Copy(_initial.Data, working.Data, _initial.Data.Length);

        double currentZ = StockTopHeight;
        double? lastX = null, lastY = null;

        foreach (var rawLine in toolpathGcode)
        {
            if (shouldCancel())
            {
                return new SimulationResult { FinalHeightmap = working, SimulationTimeSeconds = sw.Elapsed.TotalSeconds, IsCancelled = true };
            }
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith('(') || trimmed.Length == 0 || trimmed.StartsWith('%') || trimmed.StartsWith("O="))
            {
                continue;
            }

            if (trimmed.StartsWith("G0 ") || trimmed.StartsWith("G00"))
            {
                foreach (var comp in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (comp.StartsWith('X') && TryD(comp[1..], out var x)) lastX = x;
                    else if (comp.StartsWith('Y') && TryD(comp[1..], out var y)) lastY = y;
                    else if (comp.StartsWith('Z') && TryD(comp[1..], out var z)) currentZ = z;
                }
                continue;
            }

            if (trimmed.StartsWith("G1 ") || trimmed.StartsWith("G01"))
            {
                double? xc = null, yc = null, zc = null;
                foreach (var comp in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (comp.StartsWith('X') && TryD(comp[1..], out var x)) xc = x;
                    else if (comp.StartsWith('Y') && TryD(comp[1..], out var y)) yc = y;
                    else if (comp.StartsWith('Z') && TryD(comp[1..], out var z)) zc = z;
                }
                if (zc is { } zVal) currentZ = zVal;

                double? endX = xc ?? lastX;
                double? endY = yc ?? lastY;
                if (currentZ >= StockTopHeight || endX is not { } ex || endY is not { } ey) continue;

                double startX = lastX ?? ex;
                double startY = lastY ?? ey;
                double dx = ex - startX, dy = ey - startY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)Math.Ceiling(dist / working.CellSizeMm));
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double wx = startX + dx * t;
                    double wy = startY + dy * t;
                    var (gx, gy) = working.GridPosition(wx, wy);
                    double currentHeight = working.GetHeight(gx, gy);
                    if (currentZ < currentHeight)
                    {
                        working.SetHeight(currentZ, gx, gy);
                    }
                }
                if (endX is { }) lastX = endX;
                if (endY is { }) lastY = endY;
            }
        }

        return new SimulationResult { FinalHeightmap = working, SimulationTimeSeconds = sw.Elapsed.TotalSeconds };
    }

    /// <summary>Coarse height samples for draft preview (ported from draftHeightSamples).</summary>
    public static (List<(double X, double Y, double Z)> Samples, double Seconds) DraftHeightSamples(
        IReadOnlyList<string> gcodeLines, double cellSizeMm = 2.0, double stockMm = 120, int sampleStride = 0)
    {
        var sim = CreateDefault(cellSizeMm: cellSizeMm, stockWidthMm: stockMm, stockHeightMm: stockMm);
        var result = sim.Simulate(gcodeLines);
        var hm = result.FinalHeightmap;
        int step = sampleStride > 0 ? sampleStride : Math.Max(1, hm.Width / 40);
        var samples = new List<(double, double, double)>();
        for (int gy = 0; gy < hm.Height; gy += step)
        {
            for (int gx = 0; gx < hm.Width; gx += step)
            {
                var (wx, wy) = hm.WorldPosition(gx, gy);
                samples.Add((wx, wy, hm.GetHeight(gx, gy)));
            }
        }
        return (samples, result.SimulationTimeSeconds);
    }

    private static bool TryD(string s, out double v)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}

/// <summary>Wireframe segment renderer (ported from WireframeRenderer.swift).</summary>
public static class WireframeRenderer
{
    public sealed record ParsedMove(double X, double Y, bool IsRapid);

    /// <summary>Parse a modal XY position from a motion line (G0 X10 Y20 and G0X10Y20 forms).</summary>
    public static ParsedMove? ParseXY(string line, double? previousX, double? previousY)
    {
        var trimmed = line.Trim().ToUpperInvariant();
        if (!(trimmed.StartsWith("G0") || trimmed.StartsWith("G1") || trimmed.StartsWith("G00") || trimmed.StartsWith("G01")))
        {
            return null;
        }
        if (trimmed.StartsWith("G2") || trimmed.StartsWith("G3") || trimmed.StartsWith("G02") || trimmed.StartsWith("G03"))
        {
            return null;
        }
        bool isRapid = trimmed.StartsWith("G0") && !trimmed.StartsWith("G01") && !trimmed.StartsWith("G1");

        double? x = previousX, y = previousY;
        int i = 0;
        while (i < trimmed.Length)
        {
            char ch = trimmed[i];
            if (ch is 'X' or 'Y')
            {
                char axis = ch;
                i++;
                int start = i;
                while (i < trimmed.Length)
                {
                    char c = trimmed[i];
                    if (c == '-' || c == '+' || c == '.' || char.IsDigit(c)) i++;
                    else break;
                }
                if (double.TryParse(trimmed[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                {
                    if (axis == 'X') x = val; else y = val;
                }
                continue;
            }
            i++;
        }
        if (x is not { } xx || y is not { } yy) return null;
        return new ParsedMove(xx, yy, isRapid);
    }

    public sealed record Segment(VectorPilot.Geometry.VectorPoint Start, VectorPilot.Geometry.VectorPoint End, bool IsRapid);

    /// <summary>Generate segments with rapid/cut coloring (modal XYZ aware).</summary>
    public static List<Segment> GenerateSegments(IReadOnlyList<string> gcodeLines, Func<bool>? shouldCancel = null)
    {
        shouldCancel ??= () => false;
        var segments = new List<Segment>();
        double? lastX = null, lastY = null;
        foreach (var line in gcodeLines)
        {
            if (shouldCancel()) break;
            var parsed = ParseXY(line, lastX, lastY);
            if (parsed is null) continue;
            var current = new VectorPilot.Geometry.VectorPoint(parsed.X, parsed.Y);
            if (lastX is { } lx && lastY is { } ly)
            {
                segments.Add(new Segment(new VectorPilot.Geometry.VectorPoint(lx, ly), current, parsed.IsRapid));
            }
            lastX = parsed.X;
            lastY = parsed.Y;
        }
        return segments;
    }
}
