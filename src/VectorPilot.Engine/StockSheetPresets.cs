namespace VectorPilot.Engine;

/// <summary>
/// Stock sheet preset (ported from StockSheetPreset.swift, SPK-1132).
/// 72 presets: 6 imperial × 6 thickness + 6 metric × 6 thickness.
/// </summary>
public sealed class StockSheetPreset
{
    public string Name { get; set; } = "4'x8'x0.375''";
    public double WidthMM { get; set; } = 1219.2;
    public double DepthMM { get; set; } = 2438.4;
    public double ThicknessMM { get; set; } = 9.525;
    public bool IsMetric { get; set; }
}

/// <summary>The 72 shipped stock sheet presets (SPK-1132).</summary>
public static class StockSheetPresets
{
    public static readonly List<(string Name, double Width, double Depth)> ImperialSizes = new()
    {
        ("2'x2'", 609.6, 609.6),
        ("2'x4'", 609.6, 1219.2),
        ("4'x2'", 1219.2, 609.6),
        ("4'x4'", 1219.2, 1219.2),
        ("4'x8'", 1219.2, 2438.4),
        ("8'x4'", 2438.4, 1219.2)
    };

    public static readonly List<(string Label, double MM)> ImperialThicknesses = new()
    {
        ("0.125", 3.175), ("0.25", 6.35), ("0.375", 9.525),
        ("0.5", 12.7), ("0.75", 19.05), ("1", 25.4)
    };

    public static readonly List<(string Name, double Width, double Depth)> MetricSizes = new()
    {
        ("610x610", 610, 610),
        ("610x1219", 610, 1219),
        ("1219x610", 1219, 610),
        ("1219x1219", 1219, 1219),
        ("1219x2438", 1219, 2438),
        ("2438x1219", 2438, 1219)
    };

    public static readonly List<double> MetricThicknesses = new() { 3, 6, 9, 12, 18, 25 };

    public static List<StockSheetPreset> All { get; } = Build();

    private static List<StockSheetPreset> Build()
    {
        var result = new List<StockSheetPreset>();
        foreach (var size in ImperialSizes)
        {
            foreach (var t in ImperialThicknesses)
            {
                result.Add(new StockSheetPreset
                {
                    Name = $"{size.Name}x{t.Label}''",
                    WidthMM = size.Width,
                    DepthMM = size.Depth,
                    ThicknessMM = t.MM,
                    IsMetric = false
                });
            }
        }
        foreach (var size in MetricSizes)
        {
            foreach (var t in MetricThicknesses)
            {
                result.Add(new StockSheetPreset
                {
                    Name = $"{size.Name}x{(int)t} mm",
                    WidthMM = size.Width,
                    DepthMM = size.Depth,
                    ThicknessMM = t,
                    IsMetric = true
                });
            }
        }
        return result;
    }

    public static List<StockSheetPreset> Imperial => All.Where(p => !p.IsMetric).ToList();
    public static List<StockSheetPreset> Metric => All.Where(p => p.IsMetric).ToList();

    public static StockSheetPreset? PresetByName(string name) => All.FirstOrDefault(p => p.Name == name);

    /// <summary>Apply a preset to a sheet.</summary>
    public static void Apply(StockSheetPreset preset, Sheet sheet)
    {
        sheet.Name = preset.Name;
        sheet.Width = preset.WidthMM;
        sheet.Height = preset.DepthMM;
        sheet.Thickness = preset.ThicknessMM;
    }
}
