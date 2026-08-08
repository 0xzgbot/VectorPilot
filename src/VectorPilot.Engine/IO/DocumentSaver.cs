namespace VectorPilot.Engine.IO;

/// <summary>
/// Saves/loads the .shoppilot package directory (mirrors DocumentSaver.swift /
/// DocumentLoader.swift). Layout: manifest.json + toolpaths.json + sheets/&lt;id&gt;.json.
/// Unknown keys in loaded JSON are tolerated (forward-compatible).
/// </summary>
public static class DocumentSaver
{
    /// <summary>Save a job (and its toolpaths) to `path`; appends .shoppilot when missing.</summary>
    public static void Save(Job job, IEnumerable<PersistedToolpath> toolpaths, string path)
    {
        var dir = path.EndsWith(".shoppilot", StringComparison.OrdinalIgnoreCase) ? path : path + ".shoppilot";
        Directory.CreateDirectory(Path.Combine(dir, "sheets"));

        var manifest = DocumentJson.ToManifest(job);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), Json.Serialize(manifest, DocumentJson.Options));

        var tpList = toolpaths.ToList();
        File.WriteAllText(Path.Combine(dir, "toolpaths.json"), Json.Serialize(tpList, DocumentJson.Options));

        foreach (var sheet in job.Sheets)
        {
            var dto = DocumentJson.ToSheet(sheet);
            File.WriteAllText(
                Path.Combine(dir, "sheets", $"{sheet.Id}.json"),
                Json.Serialize(dto, DocumentJson.Options));
        }
    }

    public static bool Exists(string path)
        => Directory.Exists(path) || (path.EndsWith(".shoppilot", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path));
}

/// <summary>Loads a .shoppilot package. Never throws on a bad file — returns a result.</summary>
public static class DocumentLoader
{
    public sealed record LoadResult(Job? Job, List<PersistedToolpath>? Toolpaths, string? Error);

    public static LoadResult Load(string path)
    {
        try
        {
            var dir = path.EndsWith(".shoppilot", StringComparison.OrdinalIgnoreCase) ? path : path + ".shoppilot";
            if (!Directory.Exists(dir))
            {
                return new LoadResult(null, null, $"Package not found: {dir}");
            }

            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new LoadResult(null, null, "Package has no manifest.json");
            }
            var manifest = Json.Deserialize<ShopPilotManifest>(File.ReadAllText(manifestPath), DocumentJson.Options);
            if (manifest is null)
            {
                return new LoadResult(null, null, "manifest.json could not be parsed");
            }
            var job = DocumentJson.FromManifest(manifest);

            var sheetsDir = Path.Combine(dir, "sheets");
            if (Directory.Exists(sheetsDir))
            {
                foreach (var sheetFile in Directory.GetFiles(sheetsDir, "*.json").OrderBy(f => f))
                {
                    var dto = Json.Deserialize<DtoSheet>(File.ReadAllText(sheetFile), DocumentJson.Options);
                    if (dto is null) continue;
                    job.Sheets.Add(DocumentJson.FromSheet(dto));
                }
            }
            if (job.Sheets.Count == 0) job.Sheets.Add(new Sheet());

            List<PersistedToolpath>? toolpaths = null;
            var tpPath = Path.Combine(dir, "toolpaths.json");
            if (File.Exists(tpPath))
            {
                toolpaths = Json.Deserialize<List<PersistedToolpath>>(File.ReadAllText(tpPath), DocumentJson.Options) ?? new List<PersistedToolpath>();
            }

            return new LoadResult(job, toolpaths, null);
        }
        catch (Exception ex)
        {
            return new LoadResult(null, null, $"Load failed: {ex.Message}");
        }
    }
}

// Tiny JSON shims to keep the IO surface dependency-light (System.Text.Json is in the framework).
internal static class Json
{
    public static string Serialize<T>(T value, System.Text.Json.JsonSerializerOptions options)
        => System.Text.Json.JsonSerializer.Serialize(value, options);

    public static T? Deserialize<T>(string text, System.Text.Json.JsonSerializerOptions options)
        => System.Text.Json.JsonSerializer.Deserialize<T>(text, options);
}
