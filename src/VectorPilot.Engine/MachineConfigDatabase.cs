using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

/// <summary>
/// Machine configuration DB (Aspire Machine Configuration parity; engine side):
/// a JSON catalog of machine profiles (work envelope, axes, controller, port).
/// </summary>
public sealed class MachineConfigEntry
{
    public string Name { get; set; } = "";
    public double TravelXmm { get; set; } = 600;
    public double TravelYmm { get; set; } = 400;
    public double TravelZmm { get; set; } = 80;
    public int Axes { get; set; } = 3;
    public string Controller { get; set; } = "GRBL";
    public string Port { get; set; } = "COM3";
    public double MaxFeedRateMmPerMin { get; set; } = 2000;
    public double MaxRapidRateMmPerMin { get; set; } = 4000;
    public string Notes { get; set; } = "";
}

public sealed class MachineConfigDatabase
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }
    public List<MachineConfigEntry> Machines { get; private set; }

    public MachineConfigDatabase(string filePath)
    {
        FilePath = filePath;
        Machines = Load();
    }

    public MachineConfigDatabase WithDefaults()
    {
        if (Machines.Count == 0)
        {
            Machines = new List<MachineConfigEntry>
            {
                new() { Name = "Shapeoko 3", TravelXmm = 500, TravelYmm = 500, TravelZmm = 70, Controller = "GRBL", Port = "COM3", MaxFeedRateMmPerMin = 3000, MaxRapidRateMmPerMin = 6000 },
                new() { Name = "X-Carve 1000", TravelXmm = 1000, TravelYmm = 1000, TravelZmm = 65, Controller = "GRBL", Port = "COM4", MaxFeedRateMmPerMin = 2500, MaxRapidRateMmPerMin = 5000 },
                new() { Name = "OpenBuilds LEAD 1010", TravelXmm = 1000, TravelYmm = 1000, TravelZmm = 100, Controller = "GRBL", Port = "COM5", MaxFeedRateMmPerMin = 2000, MaxRapidRateMmPerMin = 4000 },
                new() { Name = "Generic 4-axis", TravelXmm = 800, TravelYmm = 400, TravelZmm = 120, Axes = 4, Controller = "GRBL", Port = "COM6", MaxFeedRateMmPerMin = 1800, MaxRapidRateMmPerMin = 3600 }
            };
            Save();
        }
        return this;
    }

    public List<MachineConfigEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<MachineConfigEntry>();
            return JsonSerializer.Deserialize<List<MachineConfigEntry>>(File.ReadAllText(FilePath), Options) ?? new List<MachineConfigEntry>();
        }
        catch
        {
            return new List<MachineConfigEntry>();
        }
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(Machines, Options));

    public MachineConfigEntry Add(MachineConfigEntry m)
    {
        Machines.Add(m);
        Save();
        return m;
    }

    public bool Delete(string name) => Machines.RemoveAll(m => m.Name == name) > 0;

    public MachineConfigEntry? Find(string name) => Machines.FirstOrDefault(m => m.Name == name);
}
