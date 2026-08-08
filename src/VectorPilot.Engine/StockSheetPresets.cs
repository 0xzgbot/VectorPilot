namespace VectorPilot.Engine;

/// <summary>A standard stock sheet size + thickness (ported from StockSheetPresets.swift, SPK-1132).</summary>
public sealed record StockSheetPreset(string Name, double WidthMm, double DepthMm, double ThicknessMm, bool IsMetric);

/// <summary>The 72 shipped stock sheet presets: 36 imperial + 36 metric.</summary>
public static class StockSheetPresets
{
    public static readonly (string Name, double Width, double Depth)[] ImperialSizes =
    {
        ("2'x2'", 609.6, 609.6),
        ("2'x4'", 609.6, 1219.2),
        ("4'x2'", 1219.2, 609.6),
        ("4'x4'", 1219.2, 1219.2),
        ("4'x8'", 1219.2, 2438.4),
        ("8'x4'", 2438.4, 1219.2)
    };

    public static readonly (string Label, double Mm)[] ImperialThicknesses =
    {
        ("0.125", 3.175),
        ("0.25", 6.35),
        ("0.375", 9.525),
        ("0.5", 12.7),
        ("0.75", 19.05),
        ("1", 25.4)
    };

    public static readonly (string Name, double Width, double Depth)[] MetricSizes =
    {
        ("610x610", 610, 610),
        ("610x1219", 610, 1219),
        ("1219x610", 1219, 610),
        ("1219x1219", 1219, 1219),
        ("1219x2438", 1219, 2438),
        ("2438x1219", 2438, 1219)
    };

    public static readonly double[] MetricThicknesses = { 3, 6, 9, 12, 18, 25 };

    public static IReadOnlyList<StockSheetPreset> All { get; } = BuildCatalog();

    public static IReadOnlyList<StockSheetPreset> Imperial => All.Where(p => !p.IsMetric).ToList();
    public static IReadOnlyList<StockSheetPreset> Metric => All.Where(p => p.IsMetric).ToList();

    public static StockSheetPreset? PresetNamed(string name) => All.FirstOrDefault(p => p.Name == name);

    /// <summary>Apply a preset to a sheet (mm values, metric units).</summary>
    public static void Apply(StockSheetPreset preset, Sheet sheet)
    {
        sheet.Name = preset.Name;
        sheet.Width = preset.WidthMm;
        sheet.Height = preset.DepthMm;
        sheet.Thickness = preset.ThicknessMm;
        sheet.Units = UnitSystem.Millimeters;
    }

    private static List<StockSheetPreset> BuildCatalog()
    {
        var result = new List<StockSheetPreset>();
        foreach (var size in ImperialSizes)
        {
            foreach (var t in ImperialThicknesses)
            {
                result.Add(new StockSheetPreset($"{size.Name}x{t.Label}''", size.Width, size.Depth, t.Mm, false));
            }
        }
        foreach (var size in MetricSizes)
        {
            foreach (var t in MetricThicknesses)
            {
                result.Add(new StockSheetPreset($"{size.Name}x{(int)t} mm", size.Width, size.Depth, t, true));
            }
        }
        return result;
    }
}
