using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>Computed strategy output (G-code + stats).</summary>
public sealed class StrategyResult
{
    public List<string> Gcode { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int FeatureCount { get; init; }
}

/// <summary>Pocket params shim — PocketEngine takes positional args; this gives it a form surface.</summary>
public sealed class PocketParams
{
    public double CutDepthMm { get; set; } = 3.0;
    public double StepDownMm { get; set; } = 2.0;
    public double StepOverPercent { get; set; } = 40;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeRateMmPerMin { get; set; } = 300;
    public double SpindleRpm { get; set; } = 12000;
    public double SafeZHeightMm { get; set; } = 5.0;
}

/// <summary>Result adapters: engine-specific result types → SpecialtyResult.</summary>
public static class StrategyAdapters
{
    public static SpecialtyResult ToSpecialty(ProfileToolpathResult r)
        => new() { GcodeLines = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.PassCount };
    public static SpecialtyResult ToSpecialty(VCarveResult r)
        => new() { GcodeLines = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.PassCount };
    public static SpecialtyResult ToSpecialty(QuickEngraveResult r)
        => new() { GcodeLines = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.PassCount };
    public static SpecialtyResult ToSpecialty(DrillResult r)
        => new() { GcodeLines = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.GcodeLines.Count(l => l.StartsWith("G8")) + 1 };
    public static SpecialtyResult ToSpecialty(HeightfieldToolpathResult r)
        => new() { GcodeLines = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.PassCount };
}

/// <summary>
/// Strategy registry: every toolpath engine exposed to the UI with a key,
/// display name, default params (JSON), and a compute delegate. CutPanel's
/// "Calculate" dispatches through here for all strategies uniformly.
/// </summary>
public sealed class StrategyRegistry
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed record Entry(
        string Key,
        string DisplayName,
        bool UsesHeightfield,
        string DefaultsJson,
        Func<IReadOnlyList<VectorShape>, HeightfieldData?, string, StrategyResult> Compute)
    {
        /// <summary>
        /// What the user sees. A record's synthesized ToString() dumps every member —
        /// including the whole DefaultsJson blob — into the combo row and into the
        /// UIAutomation name, which made the strategy list unreadable.
        /// </summary>
        public override string ToString() => DisplayName;
    }

    public List<Entry> Entries { get; } = new();

    private readonly Dictionary<string, Type> _paramTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CLR params type behind a strategy key. Lets the Cut panel build an editable
    /// form (including enum choices) without hardcoding a form per strategy.
    /// </summary>
    public Type? ParamsTypeFor(string key)
        => _paramTypes.TryGetValue(key, out var t) ? t : null;

    public StrategyRegistry()
    {
        void Add<T>(string key, string name, bool hf, Func<IReadOnlyList<VectorShape>, HeightfieldData?, T, SpecialtyResult> compute) where T : class, new()
        {
            var defaults = new T();
            string json = JsonSerializer.Serialize(defaults, Json);
            _paramTypes[key] = typeof(T);
            Entries.Add(new Entry(key, name, hf, json, (shapes, heightfield, paramsJson) =>
            {
                var p = JsonSerializer.Deserialize<T>(paramsJson, Json) ?? new T();
                var r = compute(shapes, heightfield, p);
                return new StrategyResult { Gcode = r.GcodeLines, EstimatedTimeSeconds = r.EstimatedTimeSeconds, FeatureCount = r.FeatureCount };
            }));
        }

        Add<ProfileToolpathParams>("profile", "Profile", false, (s, _, p) => StrategyAdapters.ToSpecialty(ProfileToolpathEngine.Compute(s, p)));
        Add<PocketParams>("pocket", "Pocket", false, (s, _, p) => new SpecialtyResult
        {
            GcodeLines = PocketEngine.Generate(s.ToList(), p.CutDepthMm, p.StepDownMm, p.StepOverPercent, p.FeedRateMmPerMin, p.PlungeRateMmPerMin, p.SpindleRpm, p.SafeZHeightMm).ToList()
        });
        Add<VCarveParams>("vcarve", "V-Carve", false, (s, _, p) => StrategyAdapters.ToSpecialty(VCarveEngine.Compute(s, p)));
        Add<DrillParams>("drill", "Drill", false, (s, _, p) => StrategyAdapters.ToSpecialty(DrillEngine.Compute(s.Select(ToDrillPoint).ToList(), p)));
        Add<QuickEngraveParams>("quickengrave", "Quick Engrave", false, (s, _, p) => StrategyAdapters.ToSpecialty(QuickEngraveEngine.Compute(s, p)));
        Add<QuickEngraveToolpathParams>("quickengrave2", "Quick Engrave (Specialty)", false, (s, _, p) => QuickEngraveToolpathEngine.Compute(s, p));
        Add<PrismToolpathParams>("prism", "Prism Carving", false, (s, _, p) => PrismToolpathEngine.Compute(s, p));
        Add<FlutingToolpathParams>("fluting", "Fluting", false, (s, _, p) => FlutingToolpathEngine.Compute(s, p));
        Add<ChamferToolpathParams>("chamfer", "Chamfer", false, (s, _, p) => ChamferToolpathEngine.Compute(s, p));
        Add<BevelCarvingParams>("bevel", "Bevel Carving", false, (s, _, p) => BevelCarvingEngine.Compute(s, p));
        Add<DragKnifeToolpathParams>("dragknife", "Drag Knife", false, (s, _, p) => DragKnifeToolpathEngine.Compute(s, p));
        Add<TextureToolpathParams>("texture", "Texture", false, (s, _, p) => TextureToolpathEngine.Compute(s, p));
        Add<InlayToolpathParams>("inlay-pocket", "Inlay (Pocket)", false, (s, _, p) => InlayToolpathEngine.ComputePocket(s, p));
        Add<InlayToolpathParams>("inlay-plug", "Inlay (Plug)", false, (s, _, p) => InlayToolpathEngine.ComputePlug(s, p));
        Add<LaserCutParams>("laser-cut", "Laser Cut", false, (s, _, p) => LaserCutEngine.Compute(s, p));
        Add<LaserFillParams>("laser-fill", "Laser Fill", false, (s, _, p) => LaserFillEngine.Compute(s, p));

        // E2: the last two unported strategies.
        Add<MouldingToolpathParams>("moulding", "Moulding", false, (s, _, p) =>
        {
            // Rails come from the selection: first two open paths, or the bounding
            // edges of a single path when only one is supplied.
            var rails = s.Where(v => v.Points.Count >= 2).Take(2).ToList();
            if (rails.Count == 0)
                return new SpecialtyResult { GcodeLines = new List<string>() };

            p.Rail1 = rails[0].Points.ToList();
            p.Rail2 = rails.Count > 1 ? rails[1].Points.ToList() : rails[0].Points.ToList();

            var r = MouldingToolpathEngine.Compute(p);
            return new SpecialtyResult { GcodeLines = r.GcodeLines };
        });
        Add<WeaveStrategyParams>("weave", "Weave", false, (s, _, p) =>
        {
            var bounds = s.SelectMany(v => v.Points).ToList();
            double w = bounds.Count > 0 ? bounds.Max(pt => pt.X) - bounds.Min(pt => pt.X) : p.WidthMm;
            double h = bounds.Count > 0 ? bounds.Max(pt => pt.Y) - bounds.Min(pt => pt.Y) : p.HeightMm;
            if (w <= 0 || h <= 0) { w = p.WidthMm; h = p.HeightMm; }

            var hf = WeaveReliefGenerator.Generate(
                new WeaveParams
                {
                    Pattern = p.Pattern,
                    WarpCount = p.WarpCount,
                    WeftCount = p.WeftCount,
                    ThreadSize = p.ThreadSizeMm,
                    Overlap = p.Overlap
                },
                width: w, height: h,
                cellSizeMm: p.CellSizeMm,
                threadHeight: p.ThreadHeightMm);

            // Finish over the woven relief so the strategy emits real cutting moves.
            var finish = HeightfieldFinishEngine.Compute(hf, new HeightfieldFinishParams
            {
                StepOverMm = p.StepOverMm,
                FeedRateMmPerMin = p.FeedRateMmPerMin,
                PlungeFeedRateMmPerMin = p.PlungeFeedRateMmPerMin,
                SafeZHeightMm = p.SafeZHeightMm
            });
            return StrategyAdapters.ToSpecialty(finish);
        });
        Add<HeightfieldRoughParams>("rough3d", "3D Rough", true, (_, hf, p) => hf is null ? Empty() : StrategyAdapters.ToSpecialty(HeightfieldRoughEngine.Compute(hf, p)));
        Add<HeightfieldFinishParams>("finish3d", "3D Finish", true, (_, hf, p) => hf is null ? Empty() : StrategyAdapters.ToSpecialty(HeightfieldFinishEngine.Compute(hf, p)));
        Add<PhotoVCarveToolpathParams>("photo-vcarve", "Photo V-Carve", true, (_, hf, p) => hf is null ? Empty() : PhotoVCarveEngine.Compute(hf, p));
        Add<SketchCarveToolpathParams>("sketch-carve", "Sketch Carving", true, (_, hf, p) => hf is null ? Empty() : SketchCarveEngine.Compute(hf, p));

        static SpecialtyResult Empty() => new() { GcodeLines = new List<string> { "%", "(No heightfield loaded)" }, FeatureCount = 0 };
    }

    private static DrillPoint ToDrillPoint(VectorShape s)
    {
        var c = s.Points.Count > 0 ? s.Points[0] : new VectorPoint(0, 0);
        return new DrillPoint(c.X, c.Y, 3.0);
    }

    public Entry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);
}

/// <summary>Import hub: file extension → importer dispatch (testable, UI-bound).</summary>
public sealed class ImportHub
{
    public sealed record ImportResult(List<VectorShape> Shapes, HeightfieldData? Heightfield, string Format);

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dxf"] = "DXF", ["svg"] = "SVG", ["eps"] = "EPS", ["pdf"] = "PDF",
        ["ai"] = "AI", ["dwg"] = "DWG", ["stl"] = "STL", ["obj"] = "OBJ",
        ["3mf"] = "3MF", ["png"] = "PNG", ["bmp"] = "BMP", ["jpg"] = "JPG", ["jpeg"] = "JPEG"
    };

    public static string Describe(string path)
        => Extensions.TryGetValue(Path.GetExtension(path).TrimStart('.'), out var f) ? f : "unknown";

    /// <summary>Import a file; returns shapes (vector formats) or a heightfield (3D/bitmap formats).</summary>
    public static ImportResult Import(string path, double cellSizeMm = 0.5)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        switch (ext)
        {
            case "dxf": return new ImportResult(DxfImporter.Parse(File.ReadAllText(path)), null, "DXF");
            case "svg": return new ImportResult(SvgImporter.Parse(File.ReadAllText(path)), null, "SVG");
            case "eps": return new ImportResult(EpsImporter.Parse(File.ReadAllText(path)), null, "EPS");
            case "pdf": return new ImportResult(PdfImporter.Parse(File.ReadAllText(path)), null, "PDF");
            case "ai": return new ImportResult(AiImporter.Parse(File.ReadAllText(path)), null, "AI");
            case "dwg": return new ImportResult(DwgImporter.Parse(File.ReadAllBytes(path)), null, "DWG");
            case "stl": return HeightfieldResult(StlImporter.Import(File.ReadAllBytes(path), cellSizeMm: cellSizeMm), "STL");
            case "obj": return HeightfieldResult(ObjImporter.Import(File.ReadAllBytes(path), cellSizeMm: cellSizeMm), "OBJ");
            case "3mf": return HeightfieldResult(ThreeMfImporter.Import(File.ReadAllBytes(path), cellSizeMm: cellSizeMm), "3MF");
            case "png":
            case "bmp":
            case "jpg":
            case "jpeg":
                return ImportBitmap(path, cellSizeMm);
            default:
                throw new NotSupportedException($"Unsupported format: {ext}");
        }
    }

    private static ImportResult HeightfieldResult(HeightfieldImportResult r, string format)
        => new(new List<VectorShape>(), r.Heightfield, format);

    private static ImportResult ImportBitmap(string path, double cellSizeMm)
    {
        // Grayscale the bitmap via WPF, then build a heightfield.
        var src = new BitmapImage(new Uri(path));
        var frame = BitmapFrame.Create(src);
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Gray8, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var buffer = new byte[w * h];
        converted.CopyPixels(buffer, w, 0);
        var hf = GrayscaleBitmap.FromGray(buffer, w, h, cellSizeMm, 0, 0, maxHeight: 10);
        return new ImportResult(new List<VectorShape>(), hf, "BITMAP");
    }
}
