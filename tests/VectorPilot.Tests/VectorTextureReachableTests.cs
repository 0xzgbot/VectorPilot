using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Vector texture fill — the engine DesignPanel.DoTextureFill calls.
///
/// VectorTextureEngine had NO VectorPilot.App call-site: crosshatch/dots/zigzag fills
/// existed but nothing could reach them.
/// </summary>
public class VectorTextureReachableTests
{
    private static VectorShape Rect() => VectorShape.Rectangle(0, 0, 100, 60);

    private static VectorShape Circle() => VectorShape.Circle(new VectorPoint(50, 50), 30);

    private static VectorShape OpenPath()
        => VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(50, 0), new(50, 40) }, closed: false);

    private static VectorTextureEngine.Params P(
        VectorTextureEngine.PatternKind kind = VectorTextureEngine.PatternKind.Crosshatch,
        double spacing = 4.0)
        => new() { Pattern = kind, SpacingMm = spacing, ClipToBoundary = true };

    // ---- a rectangle fill produces more than one child shape ----

    [Fact]
    public void A_Rectangle_Fill_Produces_Multiple_Child_Shapes()
    {
        var children = VectorTextureEngine.Generate(new[] { Rect() }, P());

        Assert.True(children.Count > 1,
            $"crosshatch produced {children.Count} shape(s) — a 100x60 area at 4mm needs many");
    }

    [Theory]
    [InlineData(VectorTextureEngine.PatternKind.Crosshatch)]
    [InlineData(VectorTextureEngine.PatternKind.Dots)]
    [InlineData(VectorTextureEngine.PatternKind.Zigzag)]
    public void Every_Pattern_Produces_Geometry(VectorTextureEngine.PatternKind kind)
    {
        var children = VectorTextureEngine.Generate(new[] { Rect() }, P(kind));

        Assert.NotEmpty(children);
        Assert.All(children, c => Assert.NotEmpty(c.Points));
    }

    [Fact]
    public void The_Three_Patterns_Differ()
    {
        string Sig(VectorTextureEngine.PatternKind k) => string.Join(";",
            VectorTextureEngine.Generate(new[] { Rect() }, P(k))
                .SelectMany(s => s.Points)
                .Take(40)
                .Select(p => $"{p.X:F2},{p.Y:F2}"));

        var hatch = Sig(VectorTextureEngine.PatternKind.Crosshatch);
        var dots = Sig(VectorTextureEngine.PatternKind.Dots);
        var zig = Sig(VectorTextureEngine.PatternKind.Zigzag);

        Assert.NotEqual(hatch, dots);
        Assert.NotEqual(hatch, zig);
        Assert.NotEqual(dots, zig);
    }

    [Fact]
    public void Tighter_Spacing_Makes_More_Geometry()
    {
        int loose = VectorTextureEngine.Generate(new[] { Rect() }, P(spacing: 12)).Count;
        int tight = VectorTextureEngine.Generate(new[] { Rect() }, P(spacing: 3)).Count;

        Assert.True(tight > loose, $"3mm spacing gave {tight} shapes vs {loose} at 12mm");
    }

    [Fact]
    public void A_Circle_Shape_Carries_Only_Its_Centre_So_Callers_Must_Flatten_It()
    {
        // VectorShape.Circle stores ONE point (the centre) plus Radius, so a texture engine
        // that reads Points has no boundary to clip against. The panel passes whatever the
        // user selected; this documents that a circle must be flattened to a polygon first
        // rather than silently producing an empty fill.
        var circle = Circle();
        Assert.Single(circle.Points);

        var flattened = VectorShape.Polyline(
            Enumerable.Range(0, 64).Select(i =>
            {
                double a = i / 64.0 * 2 * Math.PI;
                return new VectorPoint(50 + Math.Cos(a) * 30, 50 + Math.Sin(a) * 30);
            }).ToList(), closed: true);

        Assert.NotEmpty(VectorTextureEngine.Generate(new[] { flattened }, P()));
    }

    // ---- the fill stays inside the boundary ----

    [Fact]
    public void The_Fill_Stays_Within_The_Rectangle()
    {
        foreach (var c in VectorTextureEngine.Generate(new[] { Rect() }, P()))
            foreach (var p in c.Points)
            {
                Assert.InRange(p.X, -0.01, 100.01);
                Assert.InRange(p.Y, -0.01, 60.01);
            }
    }

    [Fact]
    public void The_Fill_Covers_More_Than_A_Corner()
    {
        var pts = VectorTextureEngine.Generate(new[] { Rect() }, P())
            .SelectMany(c => c.Points).ToList();

        Assert.True(pts.Max(p => p.X) - pts.Min(p => p.X) > 50,
            "the fill spans less than half the width");
        Assert.True(pts.Max(p => p.Y) - pts.Min(p => p.Y) > 30,
            "the fill spans less than half the height");
    }

    [Fact]
    public void No_NaN_Is_Produced()
    {
        foreach (var c in VectorTextureEngine.Generate(new[] { Rect() }, P()))
            Assert.All(c.Points, p =>
                Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "texture produced NaN"));
    }

    // ---- an open path is refused, with the panel's own rule ----

    [Fact]
    public void An_Open_Path_Is_Not_A_Fillable_Boundary()
    {
        // The guard DoTextureFill applies before calling the engine.
        var open = OpenPath();
        bool fillable = open.Closed
                     || open.Type == ShapeType.Circle
                     || open.Type == ShapeType.Rectangle;

        Assert.False(fillable, "an open polyline was treated as a fillable region");
    }

    [Fact]
    public void Closed_Shapes_Are_Fillable_Boundaries()
    {
        foreach (var s in new[] { Rect(), Circle() })
        {
            bool fillable = s.Closed || s.Type == ShapeType.Circle || s.Type == ShapeType.Rectangle;
            Assert.True(fillable, $"{s.Type} was rejected as a boundary");
        }
    }

    [Fact]
    public void No_Boundaries_Produces_No_Fill()
    {
        Assert.Empty(VectorTextureEngine.Generate(Array.Empty<VectorShape>(), P()));
    }

    // ---- undo removes the fill ----

    [Fact]
    public void Undo_Removes_Every_Fill_Shape()
    {
        var job = new Job { Name = "texture" };
        var layer = job.ActiveSheet.ActiveLayer;
        layer.AddShape(Rect());

        int before = layer.Shapes.Count;

        var undo = new UndoStack();
        var snapshot = UndoStack.Snapshot(layer);

        // What DoTextureFill does after snapshotting.
        foreach (var c in VectorTextureEngine.Generate(new[] { layer.Shapes[0] }, P()))
            layer.AddShape(c);

        Assert.True(layer.Shapes.Count > before, "no fill was added");

        undo.Push("Texture fill", layer, snapshot);
        Assert.Equal("Texture fill", undo.Undo());
        Assert.Equal(before, layer.Shapes.Count);
    }
}
