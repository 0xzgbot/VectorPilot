using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

public enum ToolpathTemplateType { Profile, Pocket, Drill, VCarve, QuickEngrave }

public static class ToolpathTemplateTypeInfo
{
    public static string DisplayName(ToolpathTemplateType t) => t switch
    {
        ToolpathTemplateType.Profile => "Profile",
        ToolpathTemplateType.Pocket => "Pocket",
        ToolpathTemplateType.Drill => "Drill",
        ToolpathTemplateType.VCarve => "V-Carve",
        _ => "Quick Engrave"
    };
}

/// <summary>A saved set of toolpath params reusable across jobs (ported from ToolpathTemplate.swift).</summary>
public sealed class ToolpathTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ToolpathTemplateType ToolpathType { get; init; }

    /// <summary>
    /// Registry key this template belongs to ("profile", "photo-vcarve", "threadmill", …).
    ///
    /// ToolpathTemplateType only covers 5 of the 24 registered strategies, so keying off
    /// it alone would make templates unusable for most of them. Empty on documents saved
    /// before this field existed; those fall back to ToolpathType.
    /// </summary>
    public string StrategyKey { get; init; } = "";

    public string ParamsJson { get; init; } = "";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

/// <summary>Persists toolpath templates as a JSON array (ported from ToolpathTemplateManager.swift).</summary>
public sealed class ToolpathTemplateManager
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }
    public List<ToolpathTemplate> Templates { get; private set; }

    public ToolpathTemplateManager(string filePath)
    {
        FilePath = filePath;
        Templates = LoadTemplates();
    }

    public ToolpathTemplate SaveTemplate(string name, ToolpathTemplateType type, string paramsJson)
        => SaveTemplate(name, type, paramsJson, strategyKey: "");

    /// <summary>Save a template tagged with its registry key, so any of the 24 strategies can use one.</summary>
    public ToolpathTemplate SaveTemplate(string name, ToolpathTemplateType type, string paramsJson, string strategyKey)
    {
        var template = new ToolpathTemplate
        {
            Name = name,
            ToolpathType = type,
            ParamsJson = paramsJson,
            StrategyKey = strategyKey
        };
        Templates.Add(template);
        SaveTemplates();
        return template;
    }

    /// <summary>Templates applicable to a registry key (falls back to legacy type-only entries).</summary>
    public List<ToolpathTemplate> ForStrategy(string strategyKey)
        => Templates.Where(t => string.Equals(t.StrategyKey, strategyKey, StringComparison.OrdinalIgnoreCase)
                             || t.StrategyKey.Length == 0)
                    .ToList();

    public List<ToolpathTemplate> LoadTemplates()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ToolpathTemplate>();
            var loaded = JsonSerializer.Deserialize<List<ToolpathTemplate>>(File.ReadAllText(FilePath), Options) ?? new List<ToolpathTemplate>();
            return loaded.OrderBy(t => t.CreatedAt).ToList();
        }
        catch
        {
            return new List<ToolpathTemplate>();
        }
    }

    public void DeleteTemplate(Guid id)
    {
        Templates.RemoveAll(t => t.Id == id);
        SaveTemplates();
    }

    public string? ApplyTemplate(Guid id) => Templates.FirstOrDefault(t => t.Id == id)?.ParamsJson;

    public List<ToolpathTemplate> TemplatesFor(ToolpathTemplateType type) => Templates.Where(t => t.ToolpathType == type).ToList();

    public bool TemplateExists(string name) => Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SaveTemplates()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Templates, Options));
        }
        catch
        {
            // Persistence failure is surfaced by the caller; never throw here.
        }
    }
}
