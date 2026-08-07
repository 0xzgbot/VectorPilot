namespace VectorPilot.Engine;

/// <summary>A sheet of stock material (mirrors ShopPilot Sheet).</summary>
public sealed class Sheet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Sheet 1";
    public double Width { get; set; } = 12;
    public double Height { get; set; } = 24;
    public double Thickness { get; set; } = 0.5;
    public UnitSystem Units { get; set; } = UnitSystem.Inches;
    public Material? Material { get; set; }
    public List<Layer> Layers { get; } = new();

    public Layer ActiveLayer { get; set; }

    public Sheet()
    {
        var first = new Layer { Name = "Layer 1" };
        Layers.Add(first);
        ActiveLayer = first;
    }

    public Layer AddLayer(string? name = null)
    {
        var layer = new Layer { Name = name ?? $"Layer {Layers.Count + 1}" };
        Layers.Add(layer);
        return layer;
    }
}
