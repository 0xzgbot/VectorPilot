namespace VectorPilot.Engine;

/// <summary>
/// Weave strategy parameters (E2). <see cref="WeaveParams"/> describes the weave
/// itself; this adds the cutting settings the toolpath registry needs, so the
/// strategy can go from pattern to real G-code in one step.
/// </summary>
public sealed class WeaveStrategyParams
{
    // --- pattern ---
    public WeavePattern Pattern { get; set; } = WeavePattern.Plain;
    public int WarpCount { get; set; } = 12;
    public int WeftCount { get; set; } = 12;
    public double ThreadSizeMm { get; set; } = 6.0;
    public double Overlap { get; set; } = 0.5;
    public double ThreadHeightMm { get; set; } = 2.0;

    // --- area (used when no geometry is selected) ---
    public double WidthMm { get; set; } = 100.0;
    public double HeightMm { get; set; } = 100.0;
    public double CellSizeMm { get; set; } = 0.5;

    // --- cutting ---
    public double StepOverMm { get; set; } = 0.8;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double SafeZHeightMm { get; set; } = 5.0;
}
