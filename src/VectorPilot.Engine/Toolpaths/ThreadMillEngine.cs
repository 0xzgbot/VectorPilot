using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Internal (nut) or external (bolt) thread.</summary>
public enum ThreadHand { RightHand, LeftHand }

/// <summary>Which side of the hole the cutter works.</summary>
public enum ThreadKind { Internal, External }

public sealed class ThreadMillParams
{
    /// <summary>Nominal major diameter of the thread (mm).</summary>
    public double NominalDiameterMm { get; set; } = 12.0;

    /// <summary>Distance between adjacent thread crests along the axis (mm).</summary>
    public double PitchMm { get; set; } = 1.75;

    /// <summary>Total depth of thread to cut below the start Z (mm, positive).</summary>
    public double ThreadDepthMm { get; set; } = 15.0;

    /// <summary>Cutter diameter (mm). Must be smaller than the nominal for internal threads.</summary>
    public double ToolDiameterMm { get; set; } = 6.0;

    /// <summary>Thread form angle — 60° for metric/UN, 55° for BSPP.</summary>
    public double ThreadAngleDegrees { get; set; } = 60.0;

    public ThreadKind Kind { get; set; } = ThreadKind.Internal;
    public ThreadHand Hand { get; set; } = ThreadHand.RightHand;

    /// <summary>Radial passes to reach full thread depth (spring passes).</summary>
    public int RadialPasses { get; set; } = 2;

    /// <summary>Points per helical revolution. Higher is smoother, longer program.</summary>
    public int SegmentsPerRevolution { get; set; } = 48;

    public double FeedRateMmPerMin { get; set; } = 400;
    public double PlungeFeedRateMmPerMin { get; set; } = 200;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double SpindleRpm { get; set; } = 3000;
}

/// <summary>
/// Thread milling. Aspire ships Thread Mill as a tool type AND a toolpath; VectorPilot
/// had the tool type in ToolDatabase (and so did the Mac) but no toolpath anywhere —
/// selecting a thread mill just cut whatever strategy you happened to pick.
///
/// The cutter orbits the hole on a helix whose axial rise per revolution equals the
/// thread pitch. Right-hand internal threads are cut climb-milling counter-clockwise
/// from the bottom up, so the tool leaves the work at the top rather than dragging
/// through a finished crest.
/// </summary>
public static class ThreadMillEngine
{
    public sealed class Result
    {
        public List<string> GcodeLines { get; init; } = new();
        public int RevolutionCount { get; init; }
        public double EstimatedTimeSeconds { get; init; }
        public string? Error { get; init; }
    }

    public static Result Compute(IReadOnlyList<VectorShape> holes, ThreadMillParams p)
    {
        if (holes.Count == 0)
            return new Result { Error = "Thread Mill needs at least one hole — select a circle or drill point." };

        if (p.PitchMm <= 0)
            return new Result { Error = "Thread pitch must be greater than zero." };

        if (p.ThreadDepthMm <= 0)
            return new Result { Error = "Thread depth must be greater than zero." };

        // An internal thread cannot be cut by a tool wider than the hole.
        double radialReach = (p.NominalDiameterMm - p.ToolDiameterMm) / 2.0;
        if (p.Kind == ThreadKind.Internal && radialReach <= 0)
        {
            return new Result
            {
                Error = $"A {p.ToolDiameterMm:0.###}mm cutter does not fit a " +
                        $"{p.NominalDiameterMm:0.###}mm internal thread."
            };
        }

        string F(double v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        var g = new List<string>
        {
            "(VectorPilot thread mill)",
            "G90", "G17", "G21",
            $"M3 S{p.SpindleRpm:F0}"
        };

        // Thread depth on the flank: half the pitch divided by tan(halfAngle) is the
        // theoretical crest-to-root height for a symmetric form.
        double halfAngle = Math.Max(1e-6, p.ThreadAngleDegrees / 2.0 * Math.PI / 180.0);
        double formDepth = (p.PitchMm / 2.0) / Math.Tan(halfAngle);

        int revsPerHole = Math.Max(1, (int)Math.Ceiling(p.ThreadDepthMm / p.PitchMm));
        int totalRevs = 0;
        double cutLength = 0;

        foreach (var hole in holes)
        {
            var centre = Centre(hole);

            for (int pass = 1; pass <= Math.Max(1, p.RadialPasses); pass++)
            {
                // Step out to full form depth over the passes.
                double t = (double)pass / Math.Max(1, p.RadialPasses);
                double radius = p.Kind == ThreadKind.Internal
                    ? radialReach - formDepth * (1 - t)
                    : (p.NominalDiameterMm + p.ToolDiameterMm) / 2.0 + formDepth * (1 - t);

                if (radius <= 0) continue;

                g.Add($"G0 Z{F(p.SafeZHeightMm)}");
                g.Add($"G0 X{F(centre.X + radius)} Y{F(centre.Y)}");

                // Start at the BOTTOM and climb: the finished crest is never re-cut.
                double zBottom = -p.ThreadDepthMm;
                g.Add($"G1 Z{F(zBottom)} F{(int)p.PlungeFeedRateMmPerMin}");

                int segs = Math.Max(8, p.SegmentsPerRevolution);
                double dir = p.Hand == ThreadHand.RightHand ? 1.0 : -1.0;

                for (int rev = 0; rev < revsPerHole; rev++)
                {
                    for (int s = 1; s <= segs; s++)
                    {
                        double frac = (double)s / segs;
                        double angle = dir * 2.0 * Math.PI * frac;
                        double z = zBottom + p.PitchMm * (rev + frac);
                        if (z > 0) z = 0;

                        double x = centre.X + Math.Cos(angle) * radius;
                        double y = centre.Y + Math.Sin(angle) * radius;
                        g.Add($"G1 X{F(x)} Y{F(y)} Z{F(z)} F{(int)p.FeedRateMmPerMin}");

                        cutLength += 2 * Math.PI * radius / segs;
                    }
                    totalRevs++;
                }

                // Retract to centre before lifting so the cutter clears the crest.
                g.Add($"G1 X{F(centre.X)} Y{F(centre.Y)} F{(int)p.FeedRateMmPerMin}");
                g.Add($"G0 Z{F(p.SafeZHeightMm)}");
            }
        }

        g.Add("M5");

        double seconds = p.FeedRateMmPerMin > 0 ? cutLength / p.FeedRateMmPerMin * 60.0 : 0;
        return new Result { GcodeLines = g, RevolutionCount = totalRevs, EstimatedTimeSeconds = seconds };
    }

    private static VectorPoint Centre(VectorShape shape)
    {
        if (shape.Type == ShapeType.Circle && shape.Points.Count > 0) return shape.Points[0];
        if (shape.Points.Count == 0) return new VectorPoint(0, 0);
        return new VectorPoint(shape.Points.Average(pt => pt.X), shape.Points.Average(pt => pt.Y));
    }
}
