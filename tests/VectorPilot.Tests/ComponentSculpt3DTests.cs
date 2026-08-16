using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Comprehensive 3D component / composite / sculpt port verification (mirrors
/// ShopPilotVerifyCombine/Sculpt/DynamicProps/ShapeTools/ComponentOps/Sweep).
/// Covers: element-wise combine values, alignment gate, sculpt falloff curve +
/// brush raise + flatten + smooth + pinch + clamp-0, component composite order
/// + visibility, dynamic props (scale x2/x0.5, 90deg tilt, fade L->R 0.75 at
/// center), shape tools (flat/angled/round/smooth), component ops (smooth
/// volume-preserve, emboss raised, bake, split), sweep (parallel rails fill,
/// circle dome peaks). EOL-safe, line endings normalized by .gitattributes.
/// </summary>
public class ComponentSculpt3DTests
{
    private static HeightfieldData Grid(int w, int h, double v, double cell = 1.0)
    {
        var arr = new double[w * h];
        Array.Fill(arr, v);
        return new HeightfieldData(w, h, cell, 0, 0, arr);
    }

    private static HeightfieldData Heights(int w, int h, double[] arr, double cell = 1.0)
        => new(w, h, cell, 0, 0, arr);

    // ---- Combine element-wise values ----

    [Fact]
    public void Combine_Add_Caps_At_MaxH()
    {
        // a=6, b=4 -> maxH=6, min(6, 6+4)=min(6,10)=6 (cap at tallest input)
        var a = Grid(4, 4, 6.0);
        var b = Grid(4, 4, 4.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineAdd)!;
        Assert.Equal(6.0, r.MaxHeight, 6);
        Assert.Equal(6.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Subtract_Clamps_At_Zero()
    {
        // a=6, b=2 -> max(0, 6-2) = 4
        var a = Grid(4, 4, 6.0);
        var b = Grid(4, 4, 2.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineSubtract)!;
        Assert.Equal(4.0, r.Heights[0], 6);
        // a - b would be negative -> clamp 0
        var a2 = Grid(4, 4, 1.0);
        var r2 = ComponentCompositor.Combine(a2, b, OperationMode.CombineSubtract)!;
        Assert.Equal(0.0, r2.Heights[0], 6);
    }

    [Fact]
    public void Combine_Merge_Keeps_Higher()
    {
        // a=6, b=2 -> max(6,2)=6
        var a = Grid(4, 4, 6.0);
        var b = Grid(4, 4, 2.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineMerge)!;
        Assert.Equal(6.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Low_Keeps_Lower()
    {
        // a=2, b=6 -> min(2,6)=2
        var a = Grid(4, 4, 2.0);
        var b = Grid(4, 4, 6.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineLow)!;
        Assert.Equal(2.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Max_Equals_Merge()
    {
        var a = Grid(4, 4, 3.0);
        var b = Grid(4, 4, 7.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineMax)!;
        Assert.Equal(7.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Min_Equals_Low()
    {
        var a = Grid(4, 4, 3.0);
        var b = Grid(4, 4, 7.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineMin)!;
        Assert.Equal(3.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Multiply_Normalized_Product()
    {
        // a=8, b=2 -> maxH=8, min(8, 8*2/8) = min(8, 2) = 2
        var a = Grid(4, 4, 8.0);
        var b = Grid(4, 4, 2.0);
        var r = ComponentCompositor.Combine(a, b, OperationMode.CombineMultiply)!;
        Assert.Equal(2.0, r.Heights[0], 6);
    }

    [Fact]
    public void Combine_Aligned_Gate_Rejects_Mismatch()
    {
        var a = Grid(4, 4, 1.0);
        var b = Grid(4, 5, 1.0);
        Assert.Null(ComponentCompositor.Combine(a, b, OperationMode.CombineAdd));
        var c = Grid(4, 4, 1.0);
        var d = Grid(4, 4, 1.0);
        d = new HeightfieldData(4, 4, 1.0, 10, 0, new double[16]); // different origin
        Assert.Null(ComponentCompositor.Combine(c, d, OperationMode.CombineAdd));
    }

    // ---- Sculpt ----

    [Fact]
    public void Sculpt_Falloff_Center_Is_One_Edge_Zero()
    {
        Assert.Equal(1.0, SculptEngine.FalloffWeight(0, BrushShape.Sphere, BrushFalloff.Constant), 6);
        Assert.Equal(0.0, SculptEngine.FalloffWeight(1, BrushShape.Sphere, BrushFalloff.Smooth), 6);
        Assert.Equal(1.0, SculptEngine.FalloffWeight(0, BrushShape.Cylinder, BrushFalloff.Constant), 6);
        Assert.True(SculptEngine.FalloffWeight(0.5, BrushShape.Sphere, BrushFalloff.Linear) > 0);
    }

    [Fact]
    public void Sculpt_Brush_Raises_Within_Radius()
    {
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Brush, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 2.0, Strength = 0.5, MaxDeltaMm = 2.0
        }, Grid(8, 8, 2.0));
        Assert.True(r.CellsAffected > 0);
        Assert.True(r.MaxHeight > 2.0);
        // Corner untouched
        Assert.Equal(2.0, r.Heightfield.HeightAt(0.5, 0.5)!.Value, 6);
        Assert.Equal(2.0, r.Heightfield.HeightAt(7.5, 7.5)!.Value, 6);
    }

    [Fact]
    public void Sculpt_Flatten_Pulls_To_Mean()
    {
        // Checkerboard: even cells 1, odd cells 5. Mean ~3.
        var h = new double[64];
        for (int i = 0; i < 64; i++) h[i] = i % 2 == 0 ? 1.0 : 5.0;
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Flatten, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 3.0, Strength = 1.0, MaxDeltaMm = 10.0
        }, Heights(8, 8, h));
        double c = r.Heightfield.HeightAt(3.5, 3.5)!.Value;
        Assert.True(c < 5.0 && c > 1.0, $"center {c}");
    }

    [Fact]
    public void Sculpt_Smooth_Blends_To_Neighbour_Avg()
    {
        // Single spike in flat field: spike at center should decrease after smooth
        var h = new double[64];
        h[27] = 10.0; // row 3, col 3 (idx = 3*8+3 = 27)
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Smooth, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 2.0, Strength = 1.0, MaxDeltaMm = 5.0
        }, Heights(8, 8, h));
        double c = r.Heightfield.HeightAt(3.5, 3.5)!.Value;
        Assert.True(c < 10.0 && c >= 0, $"spike lowered {c}");
    }

    [Fact]
    public void Sculpt_Pinch_Pulls_To_Center_Height()
    {
        // Center low, surroundings high -> pinch pulls surroundings down to center
        var h = new double[64];
        Array.Fill(h, 5.0);
        h[27] = 1.0; // center cell low
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Pinch, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 3.0, Strength = 1.0, MaxDeltaMm = 5.0
        }, Heights(8, 8, h));
        double mid = r.Heightfield.HeightAt(4.5, 3.5)!.Value;
        Assert.True(mid < 5.0, $"pinch lowered edge cell {mid}");
    }

    [Fact]
    public void Sculpt_Clamp_NonNegative()
    {
        // Deflate with high strength: must clamp at 0
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Deflate, CenterX = 3.5, CenterY = 3.5,
            RadiusMm = 2.0, Strength = 1.0, MaxDeltaMm = 100.0
        }, Grid(8, 8, 1.0));
        Assert.All(r.Heightfield.Heights, v => Assert.True(v >= 0, $"negative {v}"));
        Assert.Equal(0.0, r.MinHeight, 6);
    }

    [Fact]
    public void Sculpt_Off_Grid_Affects_Nothing()
    {
        var r = SculptEngine.ApplyStroke(new SculptStrokeParams
        {
            Tool = SculptTool.Brush, CenterX = 100, CenterY = 100,
            RadiusMm = 2.0, Strength = 0.5, MaxDeltaMm = 2.0
        }, Grid(8, 8, 2.0));
        Assert.Equal(0, r.CellsAffected);
        Assert.Equal(2.0, r.MaxHeight, 6);
    }

    // ---- Component composite order + visibility ----

    [Fact]
    public void Composite_Order_And_Visibility()
    {
        // c1 visible=Add(2), c2 visible=Add(3), c3 invisible=ignored
        var c1 = new ReliefComponent(Grid(4, 4, 2.0)) { CombineMode = OperationMode.CombineAdd, Visible = true };
        var c2 = new ReliefComponent(Grid(4, 4, 3.0)) { CombineMode = OperationMode.CombineAdd, Visible = true };
        var c3 = new ReliefComponent(Grid(4, 4, 100.0)) { CombineMode = OperationMode.CombineMax, Visible = false };
        var r = ComponentCompositor.Composite(new[] { c1, c2, c3 })!;
        // Composite starts with c1's grid (2), then Add with c2: min(maxH=3, 2+3)=3
        Assert.Equal(3.0, r.MaxHeight, 6);
    }

    [Fact]
    public void Composite_No_Visible_Returns_Null()
    {
        var c1 = new ReliefComponent(Grid(4, 4, 2.0)) { Visible = false };
        Assert.Null(ComponentCompositor.Composite(new[] { c1 }));
    }

    // ---- Dynamic props ----

    [Fact]
    public void DynamicProps_Scale_Doubles_And_Halves()
    {
        var hf = Grid(4, 4, 5.0);
        var x2 = ComponentModifierEngine.HeightScaled(hf, 2.0);
        Assert.Equal(10.0, x2.MaxHeight, 6);
        var x05 = ComponentModifierEngine.HeightScaled(hf, 0.5);
        Assert.Equal(2.5, x05.MaxHeight, 6);
    }

    [Fact]
    public void DynamicProps_Tilt_90Deg_Rotates()
    {
        // Asymmetric grid: row 0 = 1, row 4 = 5. Tilt 90deg swaps axes.
        var h = new double[25];
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
                h[j * 5 + i] = j + 1;
        var hf = Heights(5, 5, h);
        var tilted = ComponentModifierEngine.Tilted(hf, 90);
        Assert.Equal(hf.Width, tilted.Width);
        Assert.Equal(hf.Height, tilted.Height);
        Assert.True(tilted.MaxHeight > 0);
    }

    [Fact]
    public void DynamicProps_Fade_LeftToRight_Reduces_Along_X()
    {
        // 5x5 grid all 4.0. Fade amount 0.5 L->R: center col (i=2) = 1 - 0.5*2/4 = 0.75
        var hf = Grid(5, 5, 4.0);
        var f = ComponentModifierEngine.Faded(hf, amount: 0.5, FadeDirection.LeftToRight);
        double center = f.HeightAt(2.5, 2.5)!.Value;
        Assert.Equal(3.0, center, 2); // 4.0 * 0.75 = 3.0
        double left = f.HeightAt(0.5, 2.5)!.Value;
        Assert.Equal(4.0, left, 6);
        double right = f.HeightAt(4.5, 2.5)!.Value;
        Assert.Equal(2.0, right, 6);
    }

    // ---- Shape tools ----

    [Fact]
    public void Shape_Flat_Constant()
    {
        var g = ShapeReliefGenerator.Generate(ReliefShapeType.Flat, new ReliefShapeParameters(0, 0, 0, 3.0),
            width: 10, height: 10, cellSizeMm: 1.0, maxHeight: 10.0);
        Assert.Equal(3.0, g.MaxHeight, 6);
        Assert.All(g.Heights, v => Assert.Equal(3.0, v, 6));
    }

    [Fact]
    public void Shape_Angled_Ramp_Left_To_Right()
    {
        var g = ShapeReliefGenerator.Generate(ReliefShapeType.Angled, null,
            width: 10, height: 10, cellSizeMm: 1.0, maxHeight: 10.0);
        // Left edge ~0, right edge ~peak
        double left = g.HeightAt(g.MinX + 0.5, g.MinY + 5.0)!.Value;
        double right = g.HeightAt(g.MinX + 9.5, g.MinY + 5.0)!.Value;
        Assert.True(left < 2.0, $"left {left}");
        Assert.True(right > 8.0, $"right {right}");
    }

    [Fact]
    public void Shape_Round_Dome_Peaks_At_Center()
    {
        var g = ShapeReliefGenerator.Generate(ReliefShapeType.Round, null,
            width: 10, height: 10, cellSizeMm: 1.0, maxHeight: 10.0);
        double cx = g.MinX + g.Width / 2.0, cy = g.MinY + g.Height / 2.0;
        double center = g.HeightAt(cx, cy)!.Value;
        double corner = g.HeightAt(g.MinX + 0.5, g.MinY + 0.5)!.Value;
        Assert.True(center > 8.0, $"center {center}");
        // Dome falls off with distance from center; corner cell (r≈0.9) ≈ 4.36
        Assert.True(corner < 5.0 && corner > 0, $"corner {corner}");
    }

    [Fact]
    public void Shape_Smooth_Bell_Peaks_At_Center()
    {
        var g = ShapeReliefGenerator.Generate(ReliefShapeType.Smooth, new ReliefShapeParameters(0, 0, 0.5, 0),
            width: 10, height: 10, cellSizeMm: 1.0, maxHeight: 10.0);
        double cx = g.MinX + g.Width / 2.0, cy = g.MinY + g.Height / 2.0;
        double center = g.HeightAt(cx, cy)!.Value;
        Assert.True(center > 8.0, $"center {center}");
    }

    // ---- Component ops ----

    [Fact]
    public void ComponentOp_Smooth_Preserves_Volume()
    {
        // A spike should relax while mean is preserved
        var h = new double[25];
        Array.Fill(h, 2.0);
        h[12] = 10.0; // center spike
        var hf = Heights(5, 5, h);
        double origMean = hf.Heights.Average();
        var r = ComponentOperationEngine.Smooth(hf, new SmoothParams { Iterations = 10, SmoothingFactor = 0.5, PreserveVolume = true });
        double newMean = r.Heights.Average();
        Assert.True(Math.Abs(origMean - newMean) < 1e-6, $"mean drift {origMean} -> {newMean}");
        Assert.True(r.MaxHeight < 10.0, $"spike lowered");
    }

    [Fact]
    public void ComponentOp_Emboss_Raised_Adds_Dome()
    {
        var hf = Grid(5, 5, 1.0);
        var r = ComponentOperationEngine.Emboss(hf, new EmbossParams { EmbossType = EmbossType.Raised, Depth = 3.0 });
        Assert.Equal(4.0, r.HeightAt(2.5, 2.5)!.Value, 6); // center = 1 + 3
        Assert.True(r.MaxHeight >= 4.0);
    }

    [Fact]
    public void ComponentOp_Bake_Composites_Visible_Stack()
    {
        // c1=2 visible (accumulator), c2=3 visible with Add -> maxH=3, min(3, 2+3)=3
        var c1 = new ReliefComponent(Grid(4, 4, 2.0)) { CombineMode = OperationMode.CombineAdd, Visible = true };
        var c2 = new ReliefComponent(Grid(4, 4, 3.0)) { CombineMode = OperationMode.CombineAdd, Visible = true };
        var r = ComponentOperationEngine.Bake(new[] { c1, c2 })!;
        Assert.Equal(3.0, r.MaxHeight, 6);
        // Single component bake
        var single = new ReliefComponent(Grid(4, 4, 7.0)) { Visible = true };
        var rs = ComponentOperationEngine.Bake(new[] { single })!;
        Assert.Equal(7.0, rs.MaxHeight, 6);
    }

    [Fact]
    public void ComponentOp_Split_Keeps_Above_Plane_Rebased()
    {
        // Heights 0..4 across rows. Split at plane 2.0 -> keep above, rebase so min=0.
        var h = new double[25];
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
                h[j * 5 + i] = j; // 0,1,2,3,4
        var hf = Heights(5, 5, h);
        var r = ComponentOperationEngine.Split(hf, 2.0);
        Assert.Equal(0.0, r.Heights.Min(), 6); // rebased
        Assert.Equal(2.0, r.MaxHeight, 6); // was 4, minus plane 2
    }

    // ---- Sweep ----

    [Fact]
    public void Sweep_Parallel_Rails_Rectangle_Fills_Strip()
    {
        var rail1 = new List<VectorPoint> { new(0, 0), new(20, 0) };
        var rail2 = new List<VectorPoint> { new(0, 10), new(20, 10) };
        var hf = SweepReliefEngine.Sweep(rail1, rail2, SweepProfile.Rectangle, height: 5, cellSizeMm: 1.0);
        Assert.NotNull(hf);
        Assert.True(hf!.Width >= 19);
        Assert.True(hf.Height >= 9);
        Assert.Equal(5.0, hf.MaxHeight, 6);
        var center = hf.HeightAt(hf.MinX + hf.Width / 2.0, hf.MinY + hf.Height / 2.0);
        Assert.Equal(5.0, center!.Value, 6);
    }

    [Fact]
    public void Sweep_Circle_Dome_Peaks_On_Centerline()
    {
        var rail1 = new List<VectorPoint> { new(0, 0), new(20, 0) };
        var rail2 = new List<VectorPoint> { new(0, 10), new(20, 10) };
        var hf = SweepReliefEngine.Sweep(rail1, rail2, SweepProfile.Circle, height: 8, cellSizeMm: 1.0);
        Assert.NotNull(hf);
        double cx = hf!.MinX + hf.Width / 2.0, cy = hf.MinY + hf.Height / 2.0;
        double center = hf.HeightAt(cx, cy)!.Value;
        Assert.True(center > 7.0, $"center {center}");
        double edge = hf.HeightAt(cx, hf.MinY + 0.5)!.Value;
        Assert.True(edge < 1.0, $"edge {edge}");
    }

    [Fact]
    public void Sweep_Degenerate_Rails_Return_Null()
    {
        var rail2 = new List<VectorPoint> { new(0, 0), new(10, 0) };
        Assert.Null(SweepReliefEngine.Sweep(new List<VectorPoint>(), rail2, SweepProfile.Rectangle, 1));
        Assert.Null(SweepReliefEngine.Sweep(new List<VectorPoint> { new(0, 0) }, rail2, SweepProfile.Rectangle, 1));
    }
}