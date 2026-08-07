using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>One design layer holding vector shapes.</summary>
public sealed class Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer 1";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public System.Drawing.Color Color { get; set; } = System.Drawing.Color.FromArgb(0x20, 0x60, 0xC0);
    public List<VectorShape> Shapes { get; } = new();

    public void AddShape(VectorShape shape) => Shapes.Add(shape);
    public void RemoveShape(Guid id) => Shapes.RemoveAll(s => s.Id == id);
    public BoundingBox Bounds() => Shapes.Count == 0
        ? BoundingBox.Empty
        : Shapes.Skip(1).Aggregate(Shapes[0].Bounds(), (acc, s) => acc.Union(s.Bounds()));
}
