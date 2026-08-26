using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

/// <summary>
/// Material settings database (engine-side material settings):
/// JSON CRUD over Material entries with per-material feed/speed recommendations.
/// </summary>
public sealed class MaterialDatabase
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }
    public List<Material> Materials { get; private set; }

    public MaterialDatabase(string filePath)
    {
        FilePath = filePath;
        Materials = Load();
    }

    public MaterialDatabase WithDefaults()
    {
        if (Materials.Count == 0)
        {
            Materials = new List<Material>
            {
                new() { Name = "Softwood", RecommendedFeedRate = 1800, RecommendedPlungeRate = 900, RecommendedSpindleSpeed = 16000, MaxDepthOfCutMm = 8 },
                new() { Name = "Hardwood", RecommendedFeedRate = 1200, RecommendedPlungeRate = 600, RecommendedSpindleSpeed = 15000, MaxDepthOfCutMm = 6 },
                new() { Name = "MDF", RecommendedFeedRate = 1500, RecommendedPlungeRate = 700, RecommendedSpindleSpeed = 16000, MaxDepthOfCutMm = 7 },
                new() { Name = "Acrylic", RecommendedFeedRate = 1000, RecommendedPlungeRate = 400, RecommendedSpindleSpeed = 12000, MaxDepthOfCutMm = 4 },
                new() { Name = "Aluminum", RecommendedFeedRate = 500, RecommendedPlungeRate = 200, RecommendedSpindleSpeed = 10000, MaxDepthOfCutMm = 2 }
            };
            Save();
        }
        return this;
    }

    public List<Material> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Material>();
            return JsonSerializer.Deserialize<List<Material>>(File.ReadAllText(FilePath), Options) ?? new List<Material>();
        }
        catch
        {
            return new List<Material>();
        }
    }

    public void Save() { var dir = Path.GetDirectoryName(FilePath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Materials, Options)); }

    public Material Add(Material m)
    {
        Materials.Add(m);
        Save();
        return m;
    }

    public bool Delete(string name) => Materials.RemoveAll(m => m.Name == name) > 0;

    public Material? Find(string name) => Materials.FirstOrDefault(m => m.Name == name);

    /// <summary>Apply a material's recommended settings to toolpath params (feed/plunge/spindle).</summary>
    public void ApplyRecommendations(Material m, Action<double?, double?, double?> apply)
        => apply(m.RecommendedFeedRate, m.RecommendedPlungeRate, m.RecommendedSpindleSpeed);
}

/// <summary>
/// Post-processor catalog (engine-side post catalog): a JSON
/// catalog of post processors with versions tagged "Latest (V2)" and
/// install/update lifecycle.
/// </summary>
public sealed class PostDefinition
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "V1";
    public bool IsLatest { get; set; }
    public string Description { get; set; } = "";
    public string Extension { get; set; } = ".tap";
    /// <summary>G-code template tokens: [X|C|X|1.3] style grammar body.</summary>
    public string Template { get; set; } = "G21\nG90\n[HEADER]\n[BODY]\n[FOOTER]";
}

public sealed class PostCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string FilePath { get; }
    public List<PostDefinition> Posts { get; private set; }

    public PostCatalog(string filePath)
    {
        FilePath = filePath;
        Posts = Load();
    }

    public PostCatalog WithDefaults()
    {
        if (Posts.Count == 0)
        {
            Posts = new List<PostDefinition>
            {
                new() { Name = "GRBL", Version = "V2", IsLatest = true, Description = "GRBL 1.1 (generic)", Extension = ".nc" },
                new() { Name = "GRBL", Version = "V1", IsLatest = false, Description = "GRBL 1.1 (legacy)", Extension = ".nc" },
                new() { Name = "Universal G-Code", Version = "V2", IsLatest = true, Description = "Generic g-code sender", Extension = ".tap" }
            };
            Save();
        }
        return this;
    }

    public List<PostDefinition> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<PostDefinition>();
            return JsonSerializer.Deserialize<List<PostDefinition>>(File.ReadAllText(FilePath), Options) ?? new List<PostDefinition>();
        }
        catch
        {
            return new List<PostDefinition>();
        }
    }

    public void Save() { var dir = Path.GetDirectoryName(FilePath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Posts, Options)); }

    public PostDefinition? Latest(string name) => Posts.FirstOrDefault(p => p.Name == name && p.IsLatest);

    public List<PostDefinition> Versions(string name) => Posts.Where(p => p.Name == name).OrderByDescending(p => p.Version).ToList();

    /// <summary>Install or update a post (replaces same name+version, marks newest as Latest).</summary>
    public void Install(PostDefinition post)
    {
        Posts.RemoveAll(p => p.Name == post.Name && p.Version == post.Version);
        Posts.Add(post);
        var newest = Versions(post.Name).FirstOrDefault();
        foreach (var p in Posts.Where(p => p.Name == post.Name)) p.IsLatest = false;
        if (newest is not null) newest.IsLatest = true;
        Save();
    }

    public bool Remove(string name, string version) => Posts.RemoveAll(p => p.Name == name && p.Version == version) > 0;
}

/// <summary>
/// Simulation playback controller (3D-preview playback parity; engine side):
/// steps a G-code stream at a speed multiplier (2x–16x), tracking progress
/// and current position.
/// </summary>
public sealed class SimulationPlayback
{
    public IReadOnlyList<string> Lines { get; }
    public double SpeedMultiplier { get; set; } = 1.0;
    public int CurrentIndex { get; private set; }
    public double Progress => Lines.Count == 0 ? 0 : (double)CurrentIndex / Lines.Count;
    public bool IsFinished => CurrentIndex >= Lines.Count;

    public double PositionX { get; private set; }
    public double PositionY { get; private set; }
    public double PositionZ { get; private set; }

    public SimulationPlayback(IReadOnlyList<string> lines, double speedMultiplier = 1.0)
    {
        Lines = lines;
        SpeedMultiplier = Math.Clamp(speedMultiplier, 0.25, 16.0);
    }

    /// <summary>Step one line; returns the line text (or null when finished).</summary>
    public string? Step()
    {
        if (IsFinished) return null;
        var line = Lines[CurrentIndex];
        ParseMotion(line);
        CurrentIndex++;
        return line;
    }

    /// <summary>Step `count` lines at once (scaled by speed for long streams).</summary>
    public int StepMany(int count)
    {
        int n = 0;
        int scaled = Math.Max(1, (int)Math.Round(count * SpeedMultiplier));
        for (int i = 0; i < scaled && !IsFinished; i++)
        {
            Step();
            n++;
        }
        return n;
    }

    public void Restart()
    {
        CurrentIndex = 0;
        PositionX = PositionY = PositionZ = 0;
    }

    private void ParseMotion(string line)
    {
        int ix = line.IndexOf('X');
        int iy = line.IndexOf('Y');
        int iz = line.IndexOf('Z');
        if (ix >= 0) PositionX = ParseCoord(line, ix);
        if (iy >= 0) PositionY = ParseCoord(line, iy);
        if (iz >= 0) PositionZ = ParseCoord(line, iz);
    }

    private static double ParseCoord(string line, int idx)
    {
        var rest = line[(idx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
        return double.TryParse(rest, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

/// <summary>
/// Command registry (command-palette parity; engine side): named commands with
/// optional shortcuts, grouped for the palette UI.
/// </summary>
public sealed class CommandRegistry
{
    public sealed record Command(string Id, string Title, string? Shortcut, string Group, Action Execute);

    public List<Command> Commands { get; } = new();

    public void Register(Command c) => Commands.Add(c);

    public IEnumerable<Command> Search(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return Commands;
        return Commands.Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || c.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public Command? ByShortcut(string shortcut)
        => Commands.FirstOrDefault(c => string.Equals(c.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));
}
