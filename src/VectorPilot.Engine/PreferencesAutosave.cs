using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

/// <summary>
/// Preferences store (Aspire preferences parity; engine side): JSON settings
/// for units, grid, colors, autosave interval, and startup behavior.
/// </summary>
public sealed class Preferences
{
    public string Units { get; set; } = "mm";
    public bool ShowGrid { get; set; } = true;
    public double GridSpacingMm { get; set; } = 10;
    public int AutosaveIntervalSeconds { get; set; } = 300;
    public bool ShowToolpathNames { get; set; } = true;
    public bool ConfirmMachineConnect { get; set; } = true;
    public string LastOpenDirectory { get; set; } = "";
    public string Theme { get; set; } = "Dark";
}

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }
    public Preferences Value { get; private set; }

    public PreferencesStore(string filePath)
    {
        FilePath = filePath;
        Value = Load();
    }

    public Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Preferences();
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), Options) ?? new Preferences();
        }
        catch
        {
            return new Preferences();
        }
    }

    public void Save() { var dir = Path.GetDirectoryName(FilePath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Value, Options)); }

    public void Update(Action<Preferences> mutate)
    {
        mutate(Value);
        Save();
    }
}

/// <summary>
/// Autosave + crash recovery (document-model parity): periodically saves the
/// job to an .autosave file; on next launch, presence of a newer autosave than
/// the last manual save signals recoverable work.
/// </summary>
public sealed class AutosaveManager
{
    public string JobPath { get; }
    public string AutosavePath { get; }
    public TimeSpan Interval { get; set; }

    public AutosaveManager(string jobPath, TimeSpan interval)
    {
        JobPath = jobPath;
        AutosavePath = jobPath + ".autosave";
        Interval = interval;
    }

    /// <summary>Save the serialized job as an autosave.</summary>
    public void Save(string serializedJob)
    {
        var dir = Path.GetDirectoryName(AutosavePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(AutosavePath, serializedJob);
        LastAutosave = DateTime.Now;
    }

    public DateTime? LastAutosave { get; private set; }

    /// <summary>True when an autosave exists that is newer than the last manual save.</summary>
    public bool HasRecoverableWork()
    {
        if (!File.Exists(AutosavePath)) return false;
        if (!File.Exists(JobPath)) return true; // job never saved but autosave exists
        return File.GetLastWriteTimeUtc(AutosavePath) > File.GetLastWriteTimeUtc(JobPath);
    }

    /// <summary>Read the autosave content.</summary>
    public string? ReadAutosave() => File.Exists(AutosavePath) ? File.ReadAllText(AutosavePath) : null;

    /// <summary>Promote the autosave to the real job path (recovery).</summary>
    public void Recover()
    {
        if (!HasRecoverableWork()) return;
        File.Copy(AutosavePath, JobPath, overwrite: true);
    }

    /// <summary>Clear the autosave after a successful manual save.</summary>
    public void Clear() => File.Delete(AutosavePath);
}
