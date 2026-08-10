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
}
