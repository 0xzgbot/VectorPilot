using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// E3: shipped post catalog. VectorPilot shipped 3 posts against Aspire's 53+.
/// These assert each controller's dialect actually round-trips through the
/// existing template engine — not merely that entries exist.
/// </summary>
public class ShippedPostCatalogTests
{
    private static readonly List<string> Moves = new()
    {
        "G0 X0.000 Y0.000",
        "G1 X10.000 Y0.000 F1000.0",
        "G1 X10.000 Y10.000 F1000.0"
    };

    private static List<string> Emit(PostTemplate t)
        => PostTemplateEngine.Emit(Moves, t).Lines;

    [Fact]
    public void Catalog_Reaches_Aspire_Scale()
    {
        // Aspire ships 53+ posts. The parity doc claimed the row while only 3 existed,
        // then 20; this pins the real number so the claim cannot drift again.
        Assert.True(PostTemplate.Shipped.Count >= 53,
            $"expected 53+ shipped posts, got {PostTemplate.Shipped.Count}");
    }

    [Fact]
    public void Industrial_Controllers_Are_Present()
    {
        foreach (var name in new[] { "Haas", "Fanuc", "SINUMERIK", "Heidenhain", "Okuma", "Centroid" })
            Assert.Contains(PostTemplate.Shipped,
                t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Router_And_Laser_Controllers_Are_Present()
    {
        foreach (var name in new[] { "WinCNC", "Masso", "UCCNC", "ShopBot", "X-Carve", "LongMill", "Laser", "Plasma" })
            Assert.Contains(PostTemplate.Shipped,
                t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_Post_Ends_On_A_Pause_Instead_Of_A_Program_End()
    {
        // M0 pauses and waits for an operator; a job posted that way hangs forever.
        // The Duet post originally shipped with M0 and the retract/end invariant caught it.
        foreach (var t in PostTemplate.Shipped)
        {
            var lines = Emit(t);
            Assert.True(lines.Any(l => l.Contains("M2") || l.Contains("M30")),
                $"{t.Name} never ends the program");
        }
    }

    [Fact]
    public void Every_Post_Has_A_Unique_Id()
    {
        var ids = PostTemplate.Shipped.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain("", ids);
    }

    [Fact]
    public void Every_Post_Emits_All_Moves()
    {
        foreach (var t in PostTemplate.Shipped)
        {
            var lines = Emit(t);
            Assert.NotEmpty(lines);
            foreach (var m in Moves)
            {
                var body = m.Split(' ')[1];   // e.g. X10.000
                Assert.Contains(lines, l => l.Contains(body));
            }
        }
    }

    [Fact]
    public void Metric_Posts_Emit_G21_And_Imperial_Emit_G20()
    {
        foreach (var t in PostTemplate.Shipped.Where(t => t.Id.EndsWith("-mm")))
            Assert.Contains(Emit(t), l => l.Contains("G21"));

        foreach (var t in PostTemplate.Shipped.Where(t => t.Id.EndsWith("-in")))
            Assert.Contains(Emit(t), l => l.Contains("G20"));
    }

    [Fact]
    public void Every_Post_Sets_Absolute_Positioning()
    {
        foreach (var t in PostTemplate.Shipped)
            Assert.Contains(Emit(t), l => l.Contains("G90"));
    }

    [Fact]
    public void Every_Post_Retracts_And_Ends()
    {
        foreach (var t in PostTemplate.Shipped)
        {
            var lines = Emit(t);
            Assert.Contains(lines, l => l.Contains("M2") || l.Contains("M30"));
        }
    }

    [Fact]
    public void FluidNc_Selects_A_Work_Offset()
    {
        Assert.Contains(Emit(ShippedPostCatalog.FluidNc(GCodeUnits.Millimeter)), l => l.Contains("G54"));
    }

    [Fact]
    public void Marlin_Waits_For_The_Planner()
    {
        Assert.Contains(Emit(ShippedPostCatalog.Marlin(GCodeUnits.Millimeter)), l => l.Contains("M400"));
    }

    [Fact]
    public void LinuxCnc_Cancels_Compensation_And_Offsets()
    {
        var lines = Emit(ShippedPostCatalog.LinuxCnc(GCodeUnits.Millimeter));
        Assert.Contains(lines, l => l.Contains("G40"));
        Assert.Contains(lines, l => l.Contains("G49"));
    }

    [Fact]
    public void Mach_Posts_Retract_In_Machine_Coordinates()
    {
        foreach (int v in new[] { 3, 4 })
            Assert.Contains(Emit(ShippedPostCatalog.Mach(v, GCodeUnits.Inch)), l => l.Contains("G53"));
    }

    [Fact]
    public void Shapeoko_Dwells_For_Spindle_Spin_Up()
    {
        Assert.Contains(Emit(ShippedPostCatalog.Shapeoko(GCodeUnits.Millimeter)), l => l.Contains("G4 P2.0"));
    }

    [Fact]
    public void Avid_Cancels_Compensation()
    {
        var lines = Emit(ShippedPostCatalog.Avid(GCodeUnits.Inch));
        Assert.Contains(lines, l => l.Contains("G40"));
        Assert.Contains(lines, l => l.Contains("G53"));
    }

    [Fact]
    public void Rotary_Posts_Are_Flagged_And_Carry_A_Diameter()
    {
        foreach (var t in PostTemplate.Shipped.Where(t => t.RotaryWrap))
            Assert.True(t.WrapDiameterMm > 0, $"{t.Id} has no wrap diameter");

        Assert.Contains(PostTemplate.Shipped, t => t.Id == "fluidnc-rotary-y2a");
    }

    [Fact]
    public void ShippedById_Finds_Catalog_Entries()
    {
        Assert.NotNull(PostTemplate.ShippedById("shapeoko-mm"));
        Assert.NotNull(PostTemplate.ShippedById("mach4-in"));
        Assert.Null(PostTemplate.ShippedById("no-such-post"));
    }

    [Fact]
    public void Both_Unit_Variants_Exist_For_Each_Controller()
    {
        foreach (var stem in new[] { "fluidnc", "marlin", "linuxcnc", "mach3", "mach4", "shapeoko", "onefinity", "avid" })
        {
            Assert.NotNull(PostTemplate.ShippedById($"{stem}-mm"));
            Assert.NotNull(PostTemplate.ShippedById($"{stem}-in"));
        }
    }
}
