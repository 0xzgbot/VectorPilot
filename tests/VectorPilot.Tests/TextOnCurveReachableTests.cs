using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Text on curve — the engine DesignPanel.DoTextOnCurve calls.
///
/// TextOnCurve lived in VectorPilot.App with NO XAML call-site: glyph-following-a-path
/// existed but no button reached it.
/// </summary>
public class TextOnCurveReachableTests
{
    /// <summary>A 180-degree arc of radius 80 centred at (100,100).</summary>
    private static List<VectorPoint> Arc(int segments = 48)
    {
        var pts = new List<VectorPoint>();
        for (int i = 0; i <= segments; i++)
        {
            double a = Math.PI * i / segments;
            pts.Add(new VectorPoint(100 + Math.Cos(a) * 80, 100 + Math.Sin(a) * 80));
        }
        return pts;
    }

    private static List<VectorPoint> Line(double len = 300)
        => new() { new(0, 0), new(len / 2, 0), new(len, 0) };

    // ---- it produces real outlines ----

    [Fact]
    public void Text_On_An_Arc_Produces_Outlines()
    {
        var shapes = TextOnCurve.Place("CNC", Arc());

        Assert.NotEmpty(shapes);
        Assert.All(shapes, s => Assert.NotEmpty(s.Points));
    }

    [Fact]
    public void Every_Glyph_Outline_Has_Real_Geometry()
    {
        foreach (var s in TextOnCurve.Place("AB", Arc()))
        {
            double w = s.Points.Max(p => p.X) - s.Points.Min(p => p.X);
            double h = s.Points.Max(p => p.Y) - s.Points.Min(p => p.Y);
            Assert.True(w > 0 || h > 0, "a glyph outline collapsed to a point");
        }
    }

    [Fact]
    public void Outlines_Contain_No_NaN()
    {
        foreach (var s in TextOnCurve.Place("VectorPilot", Arc()))
            Assert.All(s.Points, p =>
                Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "glyph point was NaN"));
    }

    // ---- the glyphs actually follow the path ----

    [Fact]
    public void Glyphs_Land_Near_The_Arc_Not_At_The_Origin()
    {
        var shapes = TextOnCurve.Place("CNC", Arc());
        var all = shapes.SelectMany(s => s.Points).ToList();

        // The arc spans x 20..180, y 100..180. Nothing should sit at (0,0).
        Assert.True(all.Average(p => p.Y) > 60,
            $"glyphs averaged Y={all.Average(p => p.Y):F1} — they are not on the arc");
    }

    [Fact]
    public void Glyphs_Follow_A_Straight_Path_Left_To_Right()
    {
        var shapes = TextOnCurve.Place("ABCD", Line());
        Assert.True(shapes.Count >= 2, "expected at least two glyph outlines");

        double firstX = shapes.First().Points.Average(p => p.X);
        double lastX = shapes.Last().Points.Average(p => p.X);

        Assert.True(lastX > firstX,
            $"text did not advance along the path (first {firstX:F1}, last {lastX:F1})");
    }

    [Fact]
    public void A_Longer_String_Advances_Further()
    {
        double Reach(string t)
        {
            var s = TextOnCurve.Place(t, Line());
            return s.Count == 0 ? 0 : s.SelectMany(x => x.Points).Max(p => p.X);
        }

        Assert.True(Reach("ABCDEFGH") > Reach("AB"));
    }

    [Fact]
    public void Start_Offset_Shifts_The_Text_Along_The_Path()
    {
        var atStart = TextOnCurve.Place("AB", Line(), startLength: 0);
        var offset = TextOnCurve.Place("AB", Line(), startLength: 100);

        double a = atStart.SelectMany(s => s.Points).Min(p => p.X);
        double b = offset.SelectMany(s => s.Points).Min(p => p.X);

        Assert.True(b > a, $"startLength had no effect ({a:F1} vs {b:F1})");
    }

    [Fact]
    public void A_Bigger_Size_Makes_Bigger_Glyphs()
    {
        double Height(double size)
        {
            var s = TextOnCurve.Place("A", Line(), size: size);
            var pts = s.SelectMany(x => x.Points).ToList();
            return pts.Count == 0 ? 0 : pts.Max(p => p.Y) - pts.Min(p => p.Y);
        }

        Assert.True(Height(96) > Height(24));
    }

    // ---- degenerate input ----

    [Fact]
    public void Empty_Text_Produces_Nothing()
    {
        Assert.Empty(TextOnCurve.Place("", Arc()));
    }

    [Fact]
    public void A_Single_Point_Path_Produces_Nothing()
    {
        Assert.Empty(TextOnCurve.Place("AB", new List<VectorPoint> { new(0, 0) }));
    }

    [Fact]
    public void An_Empty_Path_Produces_Nothing()
    {
        Assert.Empty(TextOnCurve.Place("AB", new List<VectorPoint>()));
    }

    // ---- undo restores the layer (the panel's Snapshot/Push contract) ----

    [Fact]
    public void Undo_Restores_The_Layer_To_Its_Pre_Text_State()
    {
        var job = new Job { Name = "text" };
        var layer = job.ActiveSheet.ActiveLayer;
        var path = VectorShape.Polyline(Arc(), closed: false);
        layer.AddShape(path);

        int before = layer.Shapes.Count;

        // UndoStack is INSTANCE-based: Snapshot is static, Push/Undo are not, and there is
        // no Restore. This is the exact sequence DesignPanel.DoTextOnCurve runs.
        var undo = new UndoStack();
        var snapshot = UndoStack.Snapshot(layer);

        foreach (var g in TextOnCurve.Place("CNC", path.Points)) layer.AddShape(g);
        undo.Push("Text on curve", layer, snapshot);

        Assert.True(layer.Shapes.Count > before, "no glyphs were added");

        Assert.Equal("Text on curve", undo.Undo());
        Assert.Equal(before, layer.Shapes.Count);
    }
}
