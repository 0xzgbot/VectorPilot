using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class SpecialtyEnginesTests
{
    private static VectorShape Square(double size = 10) => VectorShape.Rectangle(0, 0, size, size);
    private static VectorShape Line() => VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(20, 0));

    [Fact]
    public void Prism_Cuts_Grooves_Across_Boundary()
    {
        var r = PrismToolpathEngine.Compute(new[] { Square() }, new PrismToolpathParams { SpacingMm = 4, VBitAngleDegrees = 90 });
        Assert.Equal("O=PRISM_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount >= 2); // multiple grooves across a 10mm square
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G1 Z-")); // V-groove depth
        Assert.Contains(r.GcodeLines, l => l == "M30");
    }

    [Fact]
    public void Fluting_Step_Down_Passes()
    {
        var r = FlutingToolpathEngine.Compute(new[] { Line() }, new FlutingToolpathParams { CutDepthMm = 4, PassDepthMm = 2 });
        Assert.Equal("O=FLUTING_TOOLPATH", r.GcodeLines[1]);
        Assert.Equal(1, r.FeatureCount);
        Assert.Contains(r.GcodeLines, l => l.Contains("pass 2/2"));
        Assert.Contains(r.GcodeLines, l => l.Contains("Z-4.000"));
    }

    [Fact]
    public void Chamfer_Depth_Follows_VBit_Angle()
    {
        var r = ChamferToolpathEngine.Compute(new[] { Line() }, new ChamferToolpathParams { ChamferWidthMm = 5, VBitAngleDegrees = 90 });
        Assert.Equal("O=CHAMFER_TOOLPATH", r.GcodeLines[1]);
        // depth = width / tan(45°) = 5
        Assert.Contains(r.GcodeLines, l => l.Contains("Z-5.000"));
    }

    [Fact]
    public void BevelCarving_Composes_Chamfer()
    {
        var r = BevelCarvingEngine.Compute(new[] { Square() }, new BevelCarvingParams { BevelWidthMm = 3, VBitAngleDegrees = 60 });
        Assert.Equal("O=CHAMFER_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount >= 1);
    }

    [Fact]
    public void QuickEngrave_Engraves_Each_Vector()
    {
        var r = QuickEngraveToolpathEngine.Compute(new[] { Line(), Line() }, new QuickEngraveToolpathParams { CutDepthMm = 1.5 });
        Assert.Equal("O=QUICK_ENGRAVE_TOOLPATH", r.GcodeLines[1]);
        Assert.Equal(2, r.FeatureCount);
    }

    [Fact]
    public void Inlay_Pocket_Uses_Flat_Bottom_VCarve()
    {
        var p = new InlayToolpathParams { VariantKind = InlayToolpathParams.Variant.Pocket, InlayDepthMm = 4, VBitAngleDegrees = 90 };
        var r = InlayToolpathEngine.ComputePocket(new[] { Square(20) }, p);
        Assert.Equal("O=V_CARVE_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l.Contains("Flat Bottom: Yes"));
    }

    [Fact]
    public void Inlay_Plug_Uses_Profile_OnCut()
    {
        var p = new InlayToolpathParams { VariantKind = InlayToolpathParams.Variant.Plug, InlayDepthMm = 4 };
        var r = InlayToolpathEngine.ComputePlug(new[] { Square(20) }, p);
        Assert.Equal("O=PROFILE_TOOLPATH", r.GcodeLines[1]);
    }

    [Fact]
    public void Inlay_Recipe_Presets_Exist()
    {
        Assert.Equal(4, VCarveInlayRecipe.Presets.Count);
        var fine = VCarveInlayRecipe.PresetNamed("Fine 30° Inlay");
        Assert.NotNull(fine);
        Assert.Equal(30, fine!.VBitAngleDegrees);
        var pp = fine.ToParams(InlayToolpathParams.Variant.Pocket);
        Assert.Equal(30, pp.VBitAngleDegrees);
        Assert.Equal(2.5, pp.InlayDepthMm);
    }

    [Fact]
    public void DragKnife_Emits_Offset_And_Pivots()
    {
        var r = DragKnifeToolpathEngine.Compute(new[] { Square(20) }, new DragKnifeToolpathParams { BladeOffsetMm = 4 });
        Assert.Equal("O=DRAG_KNIFE_TOOLPATH", r.GcodeLines[1]);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("G2") || l.StartsWith("G3")); // corner pivots
        Assert.Contains(r.GcodeLines, l => l.Contains("Pivot"));
    }

    [Fact]
    public void Texture_Parallel_Grooves()
    {
        var r = TextureToolpathEngine.Compute(new[] { Square(10) }, new TextureToolpathParams { SpacingMm = 4, VBitAngleDegrees = 90 });
        Assert.Equal("O=TEXTURE_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount >= 2);
    }

    [Fact]
    public void Texture_Crosshatch_Doubles_Passes()
    {
        var parallel = TextureToolpathEngine.Compute(new[] { Square(10) }, new TextureToolpathParams { SpacingMm = 5, PatternKind = TextureToolpathParams.Pattern.Parallel });
        var cross = TextureToolpathEngine.Compute(new[] { Square(10) }, new TextureToolpathParams { SpacingMm = 5, PatternKind = TextureToolpathParams.Pattern.Crosshatch });
        Assert.True(cross.FeatureCount > parallel.FeatureCount);
    }

    [Fact]
    public void SpecialtyBoundary_Inside_Runs()
    {
        var poly = SpecialtyBoundary.PolygonPoints(Square(10))!;
        Assert.Equal(5, poly.Count); // closed with explicit first==last
        var runs = SpecialtyBoundary.InsideRuns(poly, 5);
        Assert.Single(runs);
        Assert.Equal(0.0, runs[0].X0, 6);
        Assert.Equal(10.0, runs[0].X1, 6);
        Assert.Empty(SpecialtyBoundary.InsideRuns(poly, 15)); // outside
    }
}

public class PhotoAndSketchCarveTests
{
    /// <summary>8x8: dark block on the left half (luminance 0), bright right half.</summary>
    private static HeightfieldData TwoTone()
    {
        var h = new double[64];
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                h[j * 8 + i] = i < 4 ? 0 : 8;
        return new HeightfieldData(8, 8, 1.0, 0, 0, h);
    }

    [Fact]
    public void PhotoVCarve_Dark_Carves_Deep()
    {
        var r = PhotoVCarveEngine.Compute(TwoTone(), new PhotoVCarveToolpathParams { MaxDepthMm = 3, StepOverMm = 1 });
        Assert.Equal("O=PHOTO_V_CARVE_TOOLPATH", r.GcodeLines[1]);
        // Dark cells (h=0): z = -(stock 8) - depth 3 = -11.000; bright (h=8): z = -0.000.
        Assert.Contains(r.GcodeLines, l => l.Contains("Z-11.000"));
        Assert.Contains(r.GcodeLines, l => l.Contains("Z-0.000"));
        Assert.True(r.FeatureCount >= 4);
    }

    [Fact]
    public void PhotoVCarve_Linked_Spindle()
    {
        var r = PhotoVCarveEngine.Compute(TwoTone(), new PhotoVCarveToolpathParams { SpindleRpm = 18000 });
        Assert.Contains(r.GcodeLines, l => l == "M3 S18000");
    }

    [Fact]
    public void SketchCarve_Carves_Edges_Only()
    {
        // Vertical edge at i=3.5 → strong gradient there; flat areas untouched.
        var r = SketchCarveEngine.Compute(TwoTone(), new SketchCarveToolpathParams { MaxDepthMm = 2, StepOverMm = 1, EdgeThreshold = 0.1 });
        Assert.Equal("O=SKETCH_CARVE_TOOLPATH", r.GcodeLines[1]);
        Assert.True(r.FeatureCount > 0); // some edge cells carved
        // The deepest carve is at the edge column (near X=3.5).
        Assert.Contains(r.GcodeLines, l => l.Contains("X3.500") && l.Contains("Z-"));
    }

    [Fact]
    public void SketchCarve_Flat_Image_Carves_Nothing()
    {
        var flat = new HeightfieldData(8, 8, 1.0, 0, 0, Enumerable.Repeat(5.0, 64).ToArray());
        var r = SketchCarveEngine.Compute(flat, new SketchCarveToolpathParams { EdgeThreshold = 0.1 });
        Assert.Equal(0, r.FeatureCount);
    }
}
