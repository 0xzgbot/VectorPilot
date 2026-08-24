namespace VectorPilot.Engine;

/// <summary>
/// H-402: wasteboard surfacing. A raster facing program over the sheet XY —
/// serpentine rows at stepover spacing, one depth pass per call. The caller
/// decides when (if ever) to stream it; nothing here touches a machine.
/// </summary>
public static class WasteboardSurfacing
{
    public sealed class Params
    {
        public double SheetWidthMm { get; init; }
        public double SheetHeightMm { get; init; }
        public double CutterDiameterMm { get; init; } = 22;
        /// <summary>Stepover as a percent of cutter diameter (gSender convention).</summary>
        public double StepoverPercent { get; init; } = 40;
        public double DepthPerPassMm { get; init; } = 1;
        public double FeedRateMmPerMin { get; init; } = 800;
        public double SafeZMm { get; init; } = 5;
        public double SpindleRpm { get; init; } = 18000;
    }

    public sealed class Result
    {
        public List<string> GcodeLines { get; init; } = new();
        /// <summary>Total XY path length — feeds the time estimate.</summary>
        public double PathLengthMm { get; init; }
        public int RowCount { get; init; }
    }

    public static Result Generate(Params p)
    {
        double diameter = Math.Max(0.1, p.CutterDiameterMm);
        // gSender clamps stepover to 1–90% of the cutter.
        double stepover = Math.Clamp(p.StepoverPercent / 100.0, 0.01, 0.9) * diameter;

        var lines = new List<string>
        {
            "%",
            "O=WASTEBOARD_SURFACING",
            "(Raster facing for the wasteboard — verify stock clamps BEFORE Start)",
            $"(Sheet {p.SheetWidthMm:0.#} x {p.SheetHeightMm:0.#}mm, cutter {diameter:0.#}mm, stepover {stepover:0.##}mm)",
        };
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        lines.Add($"G0 Z{p.SafeZMm:0.###}");

        double half = diameter / 2.0;
        double x0 = half, x1 = Math.Max(half, p.SheetWidthMm - half);
        double y = half;
        double zDepth = -Math.Abs(p.DepthPerPassMm);

        double length = 0;
        int rows = 0;
        bool leftToRight = true;

        while (y <= p.SheetHeightMm - half + 1e-9 || rows == 0)
        {
            lines.Add($"G0 X{x0:0.###} Y{y:0.###}");
            lines.Add($"G1 Z{zDepth:0.###} F{(int)Math.Max(1, p.FeedRateMmPerMin / 3)}");
            double xe = leftToRight ? x1 : x0;
            lines.Add($"G1 X{xe:0.###} Y{y:0.###} F{(int)p.FeedRateMmPerMin}");
            length += Math.Abs(xe - x0);
            lines.Add($"G0 Z{p.SafeZMm:0.###}");
            rows++;

            double nextY = y + stepover;
            if (nextY > p.SheetHeightMm - half + 1e-9) break;
            y = nextY;
            leftToRight = !leftToRight;
        }

        lines.Add("M5");
        lines.Add("M30");
        lines.Add("%");

        return new Result
        {
            GcodeLines = lines,
            PathLengthMm = length,
            RowCount = rows,
        };
    }
}
