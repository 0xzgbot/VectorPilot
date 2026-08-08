namespace VectorPilot.Engine;

public enum InlayType { Pocket, Plug, FullInlay, VCarve }
public enum PlugShape { Round, Square, Hexagonal, Custom }
public enum VCaveAngle { Angle30, Angle45, Angle60, Angle90 }
public enum InlayMaterial { SameAsBase, ContrastingWood, Metal, Resin, Plastic, Custom }

/// <summary>Inlay pocket params (ported from InlayPocketParams.swift).</summary>
public sealed class InlayPocketParams
{
    public InlayType InlayType { get; set; } = InlayType.Pocket;
    public PlugShape Shape { get; set; } = PlugShape.Round;
    public double Diameter { get; set; } = 10.0;
    public double Depth { get; set; } = 3.0;
    public double PocketClearance { get; set; } = 0.02;
    public double PlugClearance { get; set; } = 0.05;
    public double ToolDiameter { get; set; } = 3.175;
    public double FeedRateMmPerMin { get; set; } = 800.0;
    public double PlungeFeedRateMmPerMin { get; set; } = 200.0;
    public VCaveAngle? VCarveAngle { get; set; }
    public double VCarveDepth { get; set; } = 2.0;
    public InlayMaterial Material { get; set; } = InlayMaterial.ContrastingWood;
    public List<(double X, double Y)> CustomShapePoints { get; set; } = new();

    public void Clamp()
    {
        Diameter = Math.Max(0.1, Diameter);
        Depth = Math.Max(0.01, Depth);
        PocketClearance = Math.Max(0.0, PocketClearance);
        PlugClearance = Math.Max(0.0, PlugClearance);
        ToolDiameter = Math.Max(0.1, ToolDiameter);
        FeedRateMmPerMin = Math.Max(1.0, FeedRateMmPerMin);
        PlungeFeedRateMmPerMin = Math.Max(1.0, PlungeFeedRateMmPerMin);
        VCarveDepth = Math.Max(0.0, VCarveDepth);
    }
}

/// <summary>V-carve inlay recipe (ported from VCarveRecipe.swift).</summary>
public sealed class VCarveRecipe
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public VCaveAngle VCarveAngle { get; init; }
    public double ToolDiameter { get; init; } = 3.175;
    public double StepOverMm { get; init; } = 0.5;
    public double FeedRateMmPerMin { get; init; } = 800.0;
    public double PlungeFeedRateMmPerMin { get; init; } = 200.0;
    public double DepthPerPassMm { get; init; } = 0.5;
    public double MaxDepthMm { get; init; } = 3.0;
    public InlayMaterial Material { get; init; } = InlayMaterial.ContrastingWood;
    public double EstimatedTimeMinutes { get; init; } = 5.0;
}

public sealed class InlayResult
{
    public InlayType InlayType { get; init; }
    public Guid? PocketId { get; init; }
    public Guid? PlugId { get; init; }
    public double ToolpathLengthMm { get; init; }
    public double EstimatedTimeMinutes { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>Inlay engine (ported from InlayEngine.swift): perimeter-based pocket/plug
/// estimation, preset V-carve recipes, validation.</summary>
public static class InlayEngine
{
    public static readonly List<VCarveRecipe> PresetRecipes = new()
    {
        new VCarveRecipe { Name = "Standard 30-Degree Inlay", Description = "Fine detail V-carve with 30-degree bit. Best for detailed lettering and small graphics.", VCarveAngle = VCaveAngle.Angle30, ToolDiameter = 3.175, StepOverMm = 0.3, FeedRateMmPerMin = 600, PlungeFeedRateMmPerMin = 150, DepthPerPassMm = 0.25, MaxDepthMm = 2.5, EstimatedTimeMinutes = 8.0 },
        new VCarveRecipe { Name = "Medium 45-Degree Inlay", Description = "Balanced detail and speed with 45-degree bit. Good for medium complexity designs.", VCarveAngle = VCaveAngle.Angle45, ToolDiameter = 3.175, StepOverMm = 0.5, FeedRateMmPerMin = 800, PlungeFeedRateMmPerMin = 200, DepthPerPassMm = 0.5, MaxDepthMm = 3.0, EstimatedTimeMinutes = 5.0 },
        new VCarveRecipe { Name = "Bold 60-Degree Inlay", Description = "Fast, bold V-carve with 60-degree bit. Ideal for large text and simple graphics.", VCarveAngle = VCaveAngle.Angle60, ToolDiameter = 6.35, StepOverMm = 0.8, FeedRateMmPerMin = 1000, PlungeFeedRateMmPerMin = 300, DepthPerPassMm = 0.75, MaxDepthMm = 4.0, EstimatedTimeMinutes = 3.5 },
        new VCarveRecipe { Name = "Deep 90-Degree Inlay", Description = "Maximum depth V-carve with 90-degree bit. For deep, dramatic shadows.", VCarveAngle = VCaveAngle.Angle90, ToolDiameter = 6.35, StepOverMm = 1.0, FeedRateMmPerMin = 1200, PlungeFeedRateMmPerMin = 400, DepthPerPassMm = 1.0, MaxDepthMm = 5.0, EstimatedTimeMinutes = 2.5 }
    };

    public static InlayResult GenerateInlay(InlayPocketParams p)
    {
        p.Clamp();
        if (p.Diameter <= 0) return new InlayResult { InlayType = p.InlayType, Success = false, ErrorMessage = "Diameter must be positive" };
        if (p.Depth <= 0) return new InlayResult { InlayType = p.InlayType, Success = false, ErrorMessage = "Depth must be positive" };

        double perimeter = p.Shape switch
        {
            PlugShape.Round => Math.PI * p.Diameter,
            PlugShape.Square => 4 * p.Diameter,
            PlugShape.Hexagonal => 6 * p.Diameter,
            _ => 2 * Math.PI * p.Diameter / 3
        };
        double clearanceFactor = 1.0 + p.PocketClearance + p.PlugClearance;
        double totalPathLength = perimeter * clearanceFactor;
        double cuttingTime = totalPathLength / p.FeedRateMmPerMin * 60.0;
        double totalTime = cuttingTime + 3.0;

        Guid? pocketId = null, plugId = null;
        switch (p.InlayType)
        {
            case InlayType.Pocket: pocketId = Guid.NewGuid(); break;
            case InlayType.Plug: plugId = Guid.NewGuid(); break;
            case InlayType.FullInlay: pocketId = Guid.NewGuid(); plugId = Guid.NewGuid(); break;
            case InlayType.VCarve: pocketId = Guid.NewGuid(); break;
        }

        return new InlayResult
        {
            InlayType = p.InlayType,
            PocketId = pocketId,
            PlugId = plugId,
            ToolpathLengthMm = totalPathLength,
            EstimatedTimeMinutes = totalTime,
            Success = true
        };
    }

    public static VCarveRecipe? GetRecipe(string name) => PresetRecipes.FirstOrDefault(r => r.Name == name);

    public static (bool IsValid, List<string> Errors) Validate(InlayPocketParams p)
    {
        var errors = new List<string>();
        if (p.Diameter <= 0) errors.Add("Diameter must be positive");
        if (p.Depth <= 0) errors.Add("Depth must be positive");
        if (p.ToolDiameter <= 0) errors.Add("Tool diameter must be positive");
        if (p.FeedRateMmPerMin <= 0) errors.Add("Feed rate must be positive");
        if (p.PlungeFeedRateMmPerMin <= 0) errors.Add("Plunge feed rate must be positive");
        if (p.PocketClearance < 0) errors.Add("Pocket clearance cannot be negative");
        if (p.PlugClearance < 0) errors.Add("Plug clearance cannot be negative");
        if (p.Shape == PlugShape.Custom && p.CustomShapePoints.Count < 3) errors.Add("Custom shape requires at least 3 points");
        return (errors.Count == 0, errors);
    }
}
