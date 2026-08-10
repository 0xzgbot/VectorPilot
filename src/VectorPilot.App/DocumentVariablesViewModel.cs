using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// Document variables panel model (SPK-0512 parity): holds the named
/// variables and driven dimensions for a document and persists them as JSON.
/// No INPC — the panel pushes/pulls values directly.
/// </summary>
public sealed class DocumentVariablesViewModel
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; }
    public List<DocumentVariable> Variables { get; } = new();
    public List<DrivenDimension> Dimensions { get; } = new();

    public DocumentVariablesViewModel(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public DocumentVariable AddVariable(string key, string value, string category = "General")
    {
        var v = new DocumentVariable { Key = key, Value = value, Category = category };
        Variables.Add(v);
        return v;
    }

    public void DeleteVariable(int index)
    {
        if (index >= 0 && index < Variables.Count) Variables.RemoveAt(index);
    }

    public DrivenDimension AddDimension(string key, string expression, string category = "Dimensions")
    {
        var d = new DrivenDimension { Key = key, Expression = expression, Category = category };
        Dimensions.Add(d);
        return d;
    }

    /// <summary>Evaluate an expression against the current variables; formatted result or "invalid expression".</summary>
    public string PreviewExpression(string expression)
    {
        var result = ExpressionCalculator.Evaluate(expression, Variables);
        return result is { } r
            ? r.ToString("0.#####", CultureInfo.InvariantCulture)
            : "invalid expression";
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var dto = new DocumentVariablesDto { Variables = Variables, Dimensions = Dimensions };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, Options));
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var dto = JsonSerializer.Deserialize<DocumentVariablesDto>(File.ReadAllText(FilePath), Options);
            if (dto is null) return;
            Variables.Clear();
            Dimensions.Clear();
            if (dto.Variables is not null) Variables.AddRange(dto.Variables);
            if (dto.Dimensions is not null) Dimensions.AddRange(dto.Dimensions);
        }
        catch
        {
            // Corrupt/missing file → start with empty lists.
        }
    }

    private sealed class DocumentVariablesDto
    {
        public List<DocumentVariable>? Variables { get; set; }
        public List<DrivenDimension>? Dimensions { get; set; }
    }
}
