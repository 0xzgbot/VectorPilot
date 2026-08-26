using System.Text.Json;
using System.Text.Json.Serialization;

namespace VectorPilot.Engine;

/// <summary>
/// Cabinetry / part-list import: vendor part-list files
/// (Mozaik, KCD, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP) map onto
/// a common part model via per-vendor parsers + a JSON schema mapper.
/// </summary>
public sealed class CabinetPart
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double ThicknessMm { get; set; }
    public int Quantity { get; set; } = 1;
    public string Material { get; set; } = "";
    public string Notes { get; set; } = "";
}

public enum PartListVendor { Mozaik, Kcd, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP }

/// <summary>
/// Part-list importer: tolerant CSV/TSV parsing with header discovery plus a
/// JSON field-mapping schema (PartListMapping.schema.json parity) so vendor
/// column names map to CabinetPart fields.
/// </summary>
public static class PartListImporter
{
    /// <summary>Default column mappings per vendor (default vendor mappings).</summary>
    public static readonly Dictionary<PartListVendor, Dictionary<string, string>> DefaultMappings = new()
    {
        [PartListVendor.Mozaik] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["partnumber"] = "Id", ["partname"] = "Name", ["width"] = "WidthMm",
            ["height"] = "HeightMm", ["thickness"] = "ThicknessMm", ["qty"] = "Quantity",
            ["material"] = "Material", ["notes"] = "Notes"
        },
        [PartListVendor.Kcd] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id", ["part"] = "Name", ["width"] = "WidthMm",
            ["height"] = "HeightMm", ["depth"] = "ThicknessMm", ["quantity"] = "Quantity",
            ["material"] = "Material"
        },
        [PartListVendor.CabinetSense] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["partid"] = "Id", ["description"] = "Name", ["w"] = "WidthMm",
            ["h"] = "HeightMm", ["t"] = "ThicknessMm", ["count"] = "Quantity"
        },
        [PartListVendor.CabinetPartsPro] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["part_number"] = "Id", ["part_name"] = "Name", ["width"] = "WidthMm",
            ["height"] = "HeightMm", ["thickness"] = "ThicknessMm", ["qty"] = "Quantity"
        },
        [PartListVendor.Polyboard] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["partid"] = "Id", ["partname"] = "Name", ["width_mm"] = "WidthMm",
            ["height_mm"] = "HeightMm", ["thickness_mm"] = "ThicknessMm", ["qty"] = "Quantity"
        },
        [PartListVendor.SmartWOP] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["partnumber"] = "Id", ["description"] = "Name", ["xsize"] = "WidthMm",
            ["ysize"] = "HeightMm", ["zsize"] = "ThicknessMm", ["quantity"] = "Quantity",
            ["material"] = "Material"
        }
    };

    /// <summary>Parse a part-list file. Auto-detects delimiter (tab vs comma).</summary>
    public static List<CabinetPart> Import(string content, PartListVendor vendor)
    {
        var mapping = DefaultMappings[vendor];
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return new List<CabinetPart>();

        char delim = lines[0].Contains('\t') ? '\t' : ',';
        var header = lines[0].Split(delim).Select(h => h.Trim().Trim('"')).ToList();
        var parts = new List<CabinetPart>();

        foreach (var line in lines.Skip(1))
        {
            var cells = SplitRow(line, delim);
            if (cells.Count < header.Count) continue;
            var part = new CabinetPart();
            bool any = false;
            for (int i = 0; i < header.Count; i++)
            {
                if (!mapping.TryGetValue(header[i], out var field)) continue;
                string value = cells[i].Trim();
                if (value.Length == 0) continue;
                SetField(part, field, value);
                any = true;
            }
            if (any) parts.Add(part);
        }
        return parts;
    }

    /// <summary>Load a custom mapping schema (JSON: {"columns": {"width": "WidthMm", ...}}).</summary>
    public static Dictionary<string, string>? LoadMappingSchema(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("columns", out var columns)) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in columns.EnumerateObject())
            {
                map[prop.Name] = prop.Value.GetString() ?? "";
            }
            return map;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> SplitRow(string line, char delim)
    {
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delim && !inQuotes) { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString());
        return cells;
    }

    private static void SetField(CabinetPart part, string field, string value)
    {
        double Number() => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        switch (field)
        {
            case "Id": part.Id = value; break;
            case "Name": part.Name = value; break;
            case "WidthMm": part.WidthMm = Number(); break;
            case "HeightMm": part.HeightMm = Number(); break;
            case "ThicknessMm": part.ThicknessMm = Number(); break;
            case "Quantity": part.Quantity = (int)Number(); break;
            case "Material": part.Material = value; break;
            case "Notes": part.Notes = value; break;
        }
    }
}

/// <summary>
/// .crv3d template system parity (clean implementation): a job template is a
/// JSON package with job setup defaults, layer definitions, and reusable
/// toolpath configs — the "New from template" surface.
/// </summary>
public static class Crv3dTemplate
{
    public sealed class ToolpathTemplateEntry
    {
        public string Name { get; set; } = "";
        public string Strategy { get; set; } = "";
        public string ParamsJson { get; set; } = "{}";
    }

    public sealed class JobTemplate
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double SheetWidthMm { get; set; } = 1220;
        public double SheetDepthMm { get; set; } = 610;
        public double MaterialThicknessMm { get; set; } = 12.7;
        public string Units { get; set; } = "mm";
        public List<ToolpathTemplateEntry> Toolpaths { get; set; } = new();
        public string PreviewLayerJson { get; set; } = "[]";
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(JobTemplate template) => JsonSerializer.Serialize(template, Options);

    public static JobTemplate? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JobTemplate>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(JobTemplate template, string path) => File.WriteAllText(path, Serialize(template));

    public static JobTemplate? Load(string path) => File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null;
}
