namespace VectorPilot.Engine;

/// <summary>
/// A user-defined document variable (ported from DocumentVariable.swift).
/// Examples: material, stock size, project name.
/// </summary>
public sealed class DocumentVariable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Category { get; set; } = "General";

    public DocumentVariable() { }
    public DocumentVariable(string key, string value, string category = "General")
    {
        Key = key; Value = value; Category = category;
    }
}

/// <summary>
/// Manages document variables + bindings (ported from DocumentVariablesModel.swift + VariableBindingManager.swift).
/// </summary>
public sealed class DocumentVariablesModel
{
    private readonly string _storagePath;
    public List<DocumentVariable> Variables { get; set; } = new();
    public List<DocumentVariableBinding> Bindings { get; set; } = new();

    public List<string> Categories => Variables.Select(v => v.Category).Distinct().OrderBy(c => c).ToList();

    public DocumentVariablesModel(string? storagePath = null)
        => _storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorPilot", "documentVariables.json");

    public void AddVariable(string key, string value, string category = "General")
        => Variables.Add(new DocumentVariable(key, value, category));

    public bool UpdateVariable(Guid id, string key, string value)
    {
        var v = Variables.FirstOrDefault(x => x.Id == id);
        if (v is null) return false;
        v.Key = key; v.Value = value; return true;
    }

    public bool DeleteVariable(Guid id) => Variables.RemoveAll(v => v.Id == id) > 0;

    public void Save()
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_storagePath, System.Text.Json.JsonSerializer.Serialize(Variables));
    }

    public bool Load()
    {
        if (!File.Exists(_storagePath)) return false;
        var loaded = System.Text.Json.JsonSerializer.Deserialize<List<DocumentVariable>>(File.ReadAllText(_storagePath));
        if (loaded is not null) Variables = loaded;
        return true;
    }

    public void AddBinding(string variableKey, string targetField, string targetObject, double? fallbackValue = null)
        => Bindings.Add(new DocumentVariableBinding(variableKey, targetField, targetObject, true, fallbackValue));

    public double? GetResolvedValue(string field, string obj)
    {
        var binding = Bindings.FirstOrDefault(b => b.TargetField == field && b.TargetObject == obj && b.Active);
        if (binding is null) return null;
        var variable = Variables.FirstOrDefault(v => v.Key == binding.VariableKey);
        if (variable is not null && double.TryParse(variable.Value, out var val)) return val;
        return binding.FallbackValue;
    }
}

/// <summary>Binds a document variable to a specific field in a job object.</summary>
public sealed class DocumentVariableBinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VariableKey { get; set; } = "";
    public string TargetField { get; set; } = "";
    public string TargetObject { get; set; } = "";
    public bool Active { get; set; } = true;
    public double? FallbackValue { get; set; }

    public DocumentVariableBinding() { }
    public DocumentVariableBinding(string key, string field, string obj, bool active, double? fallback)
    {
        VariableKey = key; TargetField = field; TargetObject = obj; Active = active; FallbackValue = fallback;
    }
}
