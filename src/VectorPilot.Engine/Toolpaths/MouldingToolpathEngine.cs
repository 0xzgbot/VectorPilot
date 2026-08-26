using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Moulding toolpath (strategy implemented from the Mac
/// building blocks — the Mac has no Moulding engine yet): sweeps a profile
/// along rails via SweepReliefEngine, then generates surface-following finish
/// G-code over the swept relief via HeightfieldFinishEngine.
/// </summary>
public sealed class MouldingToolpathParams
{
    public IReadOnlyList<VectorPoint> Rail1 { get; set; } = Array.Empty<VectorPoint>();
    public IReadOnlyList<VectorPoint> Rail2 { get; set; } = Array.Empty<VectorPoint>();
    public SweepProfile Profile { get; set; } = SweepProfile.Rectangle;
    public double HeightMm { get; set; } = 5.0;
    public double StepOverMm { get; set; } = 0.8;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double SafeZHeightMm { get; set; } = 5.0;
    public double SpindleRpm { get; set; }
    public double CellSizeMm { get; set; } = 0.5;
    public int Samples { get; set; } = 40;
}

public sealed class MouldingToolpathResult
{
    public List<string> GcodeLines { get; init; } = new();
    public HeightfieldData? Relief { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int PassCount { get; init; }
}

public static class MouldingToolpathEngine
{
    public static MouldingToolpathResult Compute(MouldingToolpathParams p)
    {
        if (p.Rail1.Count < 2 || p.Rail2.Count < 2)
        {
            return new MouldingToolpathResult { Success = false, ErrorMessage = "Moulding needs two rails (2+ points each)" };
        }

        var relief = SweepReliefEngine.Sweep(p.Rail1, p.Rail2, p.Profile, p.HeightMm, p.CellSizeMm, p.Samples);
        if (relief is null)
        {
            return new MouldingToolpathResult { Success = false, ErrorMessage = "Sweep failed to produce a relief" };
        }

        var finish = new HeightfieldFinishParams
        {
            ToolDiameterMm = 3.175,
            StepOverMm = p.StepOverMm,
            FeedRateMmPerMin = p.FeedRateMmPerMin,
            PlungeFeedRateMmPerMin = p.PlungeFeedRateMmPerMin,
            SafeZHeightMm = p.SafeZHeightMm,
            SpindleRpm = p.SpindleRpm
        };
        var result = HeightfieldFinishEngine.Compute(relief, finish);

        var lines = new List<string> { "%", "O=MOULDING_TOOLPATH" };
        lines.Add($"(Moulding: {p.Profile} profile, {p.HeightMm:0.0}mm high)");
        if (p.SpindleRpm > 0) lines.Add($"M3 S{(int)p.SpindleRpm}");
        lines.AddRange(result.GcodeLines.Skip(2)); // drop the finish header; keep motion + footer

        return new MouldingToolpathResult
        {
            GcodeLines = lines,
            Relief = relief,
            Success = true,
            PassCount = result.PassCount
        };
    }
}
