using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

/// <summary>Tool classes (ported from ToolType.swift, SPK-1133 13-class taxonomy; slotCutter kept for legacy decode).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToolType
{
    EndMill,
    RadiusedEndMill,
    BallNose,
    VBit,
    Engraving,
    RadiusedEngraving,
    Drill,
    DiamondDrag,
    Laser,
    ThreadMill,
    MultiThreadMill,
    Plasma,
    Form,
    SlotCutter
}

public static class ToolTypeExtensions
{
    public static string DisplayName(this ToolType t) => t switch
    {
        ToolType.EndMill => "End Mill",
        ToolType.RadiusedEndMill => "Radiused End Mill",
        ToolType.BallNose => "Ball Nose",
        ToolType.VBit => "V-Bit",
        ToolType.Engraving => "Engraving",
        ToolType.RadiusedEngraving => "Radiused Engraving",
        ToolType.Drill => "Drill",
        ToolType.DiamondDrag => "Diamond Drag",
        ToolType.Laser => "Laser",
        ToolType.ThreadMill => "Thread Mill",
        ToolType.MultiThreadMill => "Multi Thread Mill",
        ToolType.Plasma => "Plasma",
        ToolType.Form => "Form",
        _ => "Slot Cutter"
    };
}

/// <summary>Per-material cutting data (part of the 3-part linkage).</summary>
public sealed class ToolCutData
{
    public string Material { get; set; } = "hardwood";
    public double FeedRateMmPerMin { get; set; }
    public double PlungeRateMmPerMin { get; set; }
    public double SpindleRpm { get; set; }
    public double MaxDepthOfCutMm { get; set; }
}

/// <summary>Per-machine cutting data (machine override leg of the linkage).</summary>
public sealed class MachineCutData
{
    public string MachineName { get; set; } = "";
    public double FeedRateMmPerMin { get; set; }
    public double PlungeRateMmPerMin { get; set; }
    public double SpindleRpm { get; set; }
    public double MaxDepthOfCutMm { get; set; }
}

/// <summary>Resolved cutting data for a (tool, material, machine) triple.</summary>
public sealed record ResolvedCutData(double FeedRateMmPerMin, double PlungeRateMmPerMin, double SpindleRpm, double MaxDepthOfCutMm);

/// <summary>A tool: geometry + per-material cut data + per-machine cut data.</summary>
public sealed class Tool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ToolType Type { get; set; }
    public double DiameterMm { get; set; }
    public double CuttingLengthMm { get; set; }
    public double TotalLengthMm { get; set; }
    public double ShankDiameterMm { get; set; }
    public int Flutes { get; set; } = 2;
    public string Material { get; set; } = "carbide";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ToolCutData> CutData { get; set; } = new();
    public List<MachineCutData> MachineCutData { get; set; } = new();

    public static double RecommendedSpindleRpm(double diameterMm)
        => Math.Min(24000, Math.Max(6000, 12000 * (6.35 / Math.Max(diameterMm, 0.5))));

    public static double RecommendedDepthOfCut(double diameterMm)
        => Math.Min(2.0, Math.Max(0.5, diameterMm * 0.5));

    /// <summary>Walk the 3-part linkage: machine override → per-material → derived defaults.</summary>
    public ResolvedCutData ResolvedCutData(string? material, string? machineName)
    {
        if (machineName is not null)
        {
            var mc = MachineCutData.FirstOrDefault(m => m.MachineName.Equals(machineName, StringComparison.OrdinalIgnoreCase));
            if (mc is not null)
            {
                return new ResolvedCutData(mc.FeedRateMmPerMin, mc.PlungeRateMmPerMin, mc.SpindleRpm, mc.MaxDepthOfCutMm);
            }
        }
        if (material is not null)
        {
            var cd = CutData.FirstOrDefault(c => c.Material.Equals(material, StringComparison.OrdinalIgnoreCase));
            if (cd is not null)
            {
                return new ResolvedCutData(cd.FeedRateMmPerMin, cd.PlungeRateMmPerMin, cd.SpindleRpm, cd.MaxDepthOfCutMm);
            }
        }
        return new ResolvedCutData(
            ToolDatabase.RecommendedFeedRate(DiameterMm),
            ToolDatabase.RecommendedPlungeRate(DiameterMm),
            RecommendedSpindleRpm(DiameterMm),
            RecommendedDepthOfCut(DiameterMm));
    }
}

/// <summary>
/// Tool database with JSON persistence (ported from ToolDatabase.swift, SPK-1133/1133b).
/// Seeds the 17-entry installer-verified default catalog on first run only.
/// </summary>
public sealed class ToolDatabase
{
    public List<Tool> Tools { get; set; } = new();

    public ToolDatabase() { }

    public ToolDatabase(bool seedDefaults)
    {
        if (seedDefaults) SeedDefaults();
    }

    public void SeedDefaults()
    {
        // Seed one tool per distinct catalog entry (several strategies share a tool).
        var seen = new HashSet<string>();
        foreach (var entry in DefaultToolCatalog)
        {
            string key = $"{entry.Name}|{entry.Type}";
            if (!seen.Add(key)) continue;
            double feed = RecommendedFeedRate(entry.DiameterMm);
            double plunge = RecommendedPlungeRate(entry.DiameterMm);
            Tools.Add(new Tool
            {
                Name = entry.Name,
                Type = entry.Type,
                DiameterMm = entry.DiameterMm,
                CuttingLengthMm = Math.Max(4.0, entry.DiameterMm * 3),
                TotalLengthMm = Math.Max(20.0, entry.DiameterMm * 5),
                ShankDiameterMm = Math.Min(entry.DiameterMm, 6.35),
                Flutes = entry.Type is ToolType.VBit or ToolType.Drill or ToolType.DiamondDrag ? 1 : 2,
                CutData =
                {
                    new ToolCutData
                    {
                        Material = "hardwood",
                        FeedRateMmPerMin = feed,
                        PlungeRateMmPerMin = plunge,
                        SpindleRpm = Tool.RecommendedSpindleRpm(entry.DiameterMm),
                        MaxDepthOfCutMm = Tool.RecommendedDepthOfCut(entry.DiameterMm)
                    }
                }
            });
        }
    }

    // ---- CRUD ----

    public void Add(Tool tool)
    {
        Tools.Add(tool);
    }

    public void Remove(Guid id) => Tools.RemoveAll(t => t.Id == id);

    public void Update(Tool tool)
    {
        int idx = Tools.FindIndex(t => t.Id == tool.Id);
        if (idx >= 0)
        {
            tool.UpdatedAt = DateTime.UtcNow;
            Tools[idx] = tool;
        }
    }

    // ---- Lookup ----

    public Tool? ToolWithId(Guid id) => Tools.FirstOrDefault(t => t.Id == id);
    public List<Tool> ToolsOfTypes(IEnumerable<ToolType> types)
    {
        var set = types.ToHashSet();
        return Tools.Where(t => set.Contains(t.Type)).ToList();
    }

    public Tool? DefaultToolForStrategy(string strategy)
    {
        foreach (var entry in DefaultToolCatalog)
        {
            if (entry.Strategy.Equals(strategy, StringComparison.OrdinalIgnoreCase))
            {
                return Tools.FirstOrDefault(t => t.Name == entry.Name && t.Type == entry.Type);
            }
        }
        return null;
    }

    // ---- Persistence ----

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public void SaveToJson(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(Tools, JsonOpts));
    }

    public static ToolDatabase LoadFromJson(string path)
    {
        var db = new ToolDatabase();
        if (File.Exists(path))
        {
            var loaded = JsonSerializer.Deserialize<List<Tool>>(File.ReadAllText(path), JsonOpts);
            if (loaded is not null) db.Tools = loaded;
        }
        return db;
    }

    // ---- Recommendations ----

    public static double RecommendedFeedRate(double diameterMm, string material = "hardwood")
    {
        double factor = material.ToLowerInvariant() switch
        {
            "hardwood" => 3.0,
            "softwood" => 4.0,
            "plastic" => 5.0,
            "aluminum" => 1.5,
            "steel" => 0.8,
            _ => 3.0
        };
        return 10 * diameterMm * Math.Sqrt(diameterMm) * factor;
    }

    public static double RecommendedPlungeRate(double diameterMm, string material = "hardwood")
        => RecommendedFeedRate(diameterMm, material) * 0.4;

    // ---- SPK-1133 installer-verified default catalog: 17 entries keyed by strategy ----

    public static readonly (string Strategy, string Name, ToolType Type, double DiameterMm)[] DefaultToolCatalog =
    {
        ("Profile", "End Mill 1/4\"", ToolType.EndMill, 6.35),
        ("Pocket", "End Mill 1/8\"", ToolType.EndMill, 3.175),
        ("V-Carve", "V-Bit 90° 1¼\"", ToolType.VBit, 31.75),
        ("V-Inlay", "V-Bit 90° 1¼\"", ToolType.VBit, 31.75),
        ("3Carve", "V-Bit 60° 1/4\"", ToolType.VBit, 6.35),
        ("Finish", "Ball Nose 1/8\"", ToolType.BallNose, 3.175),
        ("Rough", "End Mill 1/4\"", ToolType.EndMill, 6.35),
        ("Drilling", "Drill 118° 1/4\"", ToolType.Drill, 6.35),
        ("Chamfer", "V-Bit 60° 1/4\"", ToolType.VBit, 6.35),
        ("Fluting", "Ball Nose 1/4\"", ToolType.BallNose, 6.35),
        ("SweptProfile", "Ball Nose 1/4\"", ToolType.BallNose, 6.35),
        ("Texture", "Ball Nose 1/4\"", ToolType.BallNose, 6.35),
        ("QuickEngrave", "Diamond Drag 90° 1/8\" 0.002\"", ToolType.DiamondDrag, 3.175),
        ("BevelCarving", "V-Bit 90° 1¼\"", ToolType.VBit, 31.75),
        ("ThreadMilling", "Thread Mill 60° 3/4\"", ToolType.ThreadMill, 19.05),
        ("LaserEngrave", "Laser Cutter 3.8W 0.3mm", ToolType.Laser, 0.3),
        ("PhotoVCarve", "V-Bit 60° 1/4\"", ToolType.VBit, 6.35)
    };
}
