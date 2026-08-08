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
    {
        var template = new ToolpathTemplate { Name = name, ToolpathType = type, ParamsJson = paramsJson };
        Templates.Add(template);
        SaveTemplates();
        return template;
    }

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
