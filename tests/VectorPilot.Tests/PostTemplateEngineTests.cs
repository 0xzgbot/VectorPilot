using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class PostTemplateEngineTests
{
    private static readonly List<string> Moves = new()
    {
        "G0 X0 Y0",
        "G1 X10 Y10 Z-2 F1000",
        "G1 X20 Y10 Z-2",
        "M3 S12000"
    };

    [Fact]
    public void Grbl_MM_Template_Expands_Moves()
    {
        var template = PostTemplate.Grbl(GCodeUnits.Millimeter);
        var r = PostTemplateEngine.Emit(Moves, template);

        Assert.Contains(r.Lines, l => l.Contains("G21 ; Millimeter units"));
        Assert.Contains(r.Lines, l => l.Contains("G90 ; Absolute positioning"));
        Assert.True(r.MoveCount >= 4);
        // Moves carry line numbers (N10, N20, ...).
        var moves = r.Lines.Where(l => l.StartsWith("N")).ToList();
        Assert.True(moves.Count >= 4);
        Assert.Contains(moves, l => l.Contains("X20.000 Y10.000 Z-2.000"));
        // Footer: retract + M2.
        Assert.Contains(r.Lines, l => l.Contains("G0 Z5.000"));
        Assert.Contains(r.Lines, l => l.Contains("M2"));
    }

    [Fact]
    public void Grbl_Inch_Template_Uses_G20()
    {
        var template = PostTemplate.Grbl(GCodeUnits.Inch);
        var r = PostTemplateEngine.Emit(Moves, template);
        Assert.Contains(r.Lines, l => l.Contains("G20 ; Inch units"));
    }

    [Fact]
    public void Rotary_Wrap_Converts_Y_To_A_Degrees()
    {
        var template = PostTemplate.GrblRotaryWrap(diameterMm: 50);
        var r = PostTemplateEngine.Emit(new List<string> { "G1 X10 Y25 Z-1 F500" }, template);

        // a = y / (π·d) · 360 = 25 / (π·50) · 360 ≈ 57.296
        var move = r.Lines.FirstOrDefault(l => l.Contains("X10"));
        Assert.NotNull(move);
        Assert.Contains("A57.296", move);
        Assert.DoesNotContain("Y25", move);
        Assert.Contains("(Y maps to A degrees about X — wrap diameter 50.0 mm)", r.Lines);
    }

    [Fact]
    public void Token_Modes_Absolute_Current_Incremental()
    {
        // Custom template exercising C (current) and I (incremental) modes.
        var template = new PostTemplate
        {
            Id = "t",
            Name = "T",
            Text = """
                (--- moves ---)
                [X|A|X|1.2] [Y|I|Y|1.2]
                (--- end ---)
                """
        };
        var r = PostTemplateEngine.Emit(new List<string> { "G1 X5 Y5", "G1 X8 Y9" }, template);
        var xys = r.Lines.Where(l => l.StartsWith("X")).ToList();
        Assert.Equal(2, xys.Count);
        Assert.Equal("X5.00 Y5.00", xys[0]);
        Assert.Equal("X8.00 Y4.00", xys[1]); // Y incremental: 9 − 5 = 4
    }

    [Fact]
    public void Shipped_Templates_Exist()
    {
        Assert.Equal(3, PostTemplate.Shipped.Count);
        Assert.NotNull(PostTemplate.ShippedById("grbl-mm"));
        Assert.NotNull(PostTemplate.ShippedById("grbl-in"));
        Assert.NotNull(PostTemplate.ShippedById("grbl-rotary-y2a"));
        Assert.Null(PostTemplate.ShippedById("nope"));
    }

    [Fact]
    public void Non_Move_Lines_Pass_Through()
    {
        var template = PostTemplate.Grbl(GCodeUnits.Millimeter);
        var r = PostTemplateEngine.Emit(new List<string> { "(comment)", "%", "O=TEST", "" }, template);
        Assert.Contains(r.Lines, l => l == "(comment)");
        Assert.Contains(r.Lines, l => l == "%");
        Assert.Contains(r.Lines, l => l == "O=TEST");
        Assert.Equal(0, r.MoveCount);
    }

    // ---- Verify1134 parity cases (exact expectations from the Mac CLT) ----

    [Fact]
    public void Verify1134_Diameter_Token_And_Per_Word_Rebuild()
    {
        var template = new PostTemplate
        {
            Id = "t",
            Text = """
                [D|A|-|1.1] mm diameter
                (--- moves ---)
                [G] [A|A|A|1.2] [X|A|X|1.3] [F|C|F|1.0]
                (--- end ---)
                """
        };
        // Move carrying an A word (rotary post output) — per-word rebuild.
        var r = PostTemplateEngine.Emit(new List<string> { "G1 X5 A6 F100" }, template);
        Assert.Contains("50.0 mm diameter", r.Lines);
        Assert.Contains("G1 A6.00 X5.000 F100", r.Lines); // verify-1134 exact golden
    }

    [Fact]
    public void Verify1134_C_Mode_Suppresses_Unchanged()
    {
        var template = new PostTemplate
        {
            Id = "t",
            Text = """
                (--- moves ---)
                [G] [X|A|X|1.3] [F|C|F|1.0]
                (--- end ---)
                """
        };
        var r = PostTemplateEngine.Emit(new List<string> { "G1 X1 F200", "G1 X2 F200", "G1 X3 F300" }, template);
        Assert.Equal("G1 X1.000 F200", r.Lines[0]);
        Assert.Equal("G1 X2.000", r.Lines[1]);            // F unchanged → suppressed
        Assert.Equal("G1 X3.000 F300", r.Lines[2]);       // F changed → re-emitted
    }

    [Fact]
    public void Verify1134_I_Mode_Emits_Deltas()
    {
        var template = new PostTemplate
        {
            Id = "t",
            Text = """
                (--- moves ---)
                [G] [X|I|X|1.3]
                (--- end ---)
                """
        };
        var r = PostTemplateEngine.Emit(new List<string> { "G1 X10", "G1 X12" }, template);
        Assert.Equal("G1 X10.000", r.Lines[0]);
        Assert.Equal("G1 X2.000", r.Lines[1]); // delta 12 − 10
    }

    [Fact]
    public void Verify1134_Sparse_Moves_Emit_Nothing_For_Missing_Words()
    {
        var template = new PostTemplate
        {
            Id = "t",
            Text = """
                (--- moves ---)
                [G] [X|A|X|1.3] [Y|A|Y|1.3] [Z|A|Z|1.3]
                (--- end ---)
                """
        };
        var r = PostTemplateEngine.Emit(new List<string> { "G0 X9" }, template);
        Assert.Equal("G0 X9.000", r.Lines[0]); // no Y/Z tokens emitted
    }

    [Fact]
    public void Verify1134_Rotary_Full_Wrap_Is_180_Degrees()
    {
        // Half circumference on Ø50 → 180°.
        var template = PostTemplate.GrblRotaryWrap(diameterMm: 50);
        var r = PostTemplateEngine.Emit(new List<string> { "G0 X0 Y78.539816" }, template);
        var line = r.Lines.FirstOrDefault(l => l.Contains("A180.000"));
        Assert.NotNull(line);
        Assert.DoesNotContain(" Y", r.Lines.First(l => l.Contains("X0.000")));
    }
}
