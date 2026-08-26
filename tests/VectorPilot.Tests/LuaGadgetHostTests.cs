using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Lua gadget host — Lua gadget host.
///
/// Gadgets are Lua; earlier VectorPilot had two hardcoded C# gadgets and no host,
/// so a user could not write one. MoonSharp runs the script in a HARD SANDBOX, which is
/// the part that matters: a gadget is untrusted code, and it must not be able to read the
/// disk, shell out, or hang the app.
/// </summary>
public class LuaGadgetHostTests
{
    // ---- it runs and draws ----

    [Fact]
    public void A_Script_Can_Draw_A_Line()
    {
        var r = LuaGadgetHost.Run("vp.line(0, 0, 50, 25)");

        Assert.True(r.Ok, r.Error);
        Assert.Single(r.Shapes);
        Assert.Equal(2, r.Shapes[0].Points.Count);
    }

    [Fact]
    public void A_Loop_Can_Draw_A_Bolt_Circle()
    {
        var r = LuaGadgetHost.Run("""
            for i = 0, 7 do
              local a = i / 8 * 2 * math.pi
              vp.circle(100 + math.cos(a) * 40, 100 + math.sin(a) * 40, 4)
            end
            """);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(8, r.Shapes.Count);
    }

    [Fact]
    public void Polyline_Accepts_A_Nested_Table()
    {
        var r = LuaGadgetHost.Run("vp.polyline({{0,0},{10,0},{10,10},{0,10}}, true)");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(4, r.Shapes[0].Points.Count);
        Assert.True(r.Shapes[0].Closed);
    }

    [Fact]
    public void Rect_Produces_Four_Corners()
    {
        var r = LuaGadgetHost.Run("vp.rect(5, 5, 40, 20)");

        Assert.True(r.Ok, r.Error);
        Assert.Equal(4, r.Shapes[0].Points.Count);
    }

    [Fact]
    public void Log_Messages_Come_Back()
    {
        var r = LuaGadgetHost.Run("vp.log('hello') vp.line(0,0,1,1)");

        Assert.True(r.Ok, r.Error);
        Assert.Contains("hello", r.Log);
    }

    [Fact]
    public void Sheet_Dimensions_Are_Exposed()
    {
        var r = LuaGadgetHost.Run("vp.rect(0, 0, vp.sheet_width(), vp.sheet_height())",
                                  sheetWidthMm: 600, sheetHeightMm: 400);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(600, r.Shapes[0].Points.Max(p => p.X), 3);
        Assert.Equal(400, r.Shapes[0].Points.Max(p => p.Y), 3);
    }

    // ---- the sandbox actually holds ----

    [Fact]
    public void The_Script_Cannot_Read_The_Filesystem()
    {
        var r = LuaGadgetHost.Run("local f = io.open('C:/Windows/win.ini') vp.line(0,0,1,1)");

        Assert.False(r.Ok, "io was reachable — the sandbox is not a sandbox");
    }

    [Fact]
    public void The_Script_Cannot_Shell_Out()
    {
        var r = LuaGadgetHost.Run("os.execute('cmd /c echo hi') vp.line(0,0,1,1)");
        Assert.False(r.Ok, "os.execute was reachable");
    }

    [Fact]
    public void The_Script_Cannot_Require_Native_Modules()
    {
        var r = LuaGadgetHost.Run("require('ffi') vp.line(0,0,1,1)");
        Assert.False(r.Ok, "require was reachable");
    }

    [Fact]
    public void An_Infinite_Loop_Is_Killed_Rather_Than_Hanging()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = LuaGadgetHost.Run("while true do end", timeoutMs: 600);
        sw.Stop();

        Assert.False(r.Ok);
        Assert.Contains("timed out", r.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds}ms to give up");
    }

    // ---- failures are reported, never faked ----

    [Fact]
    public void A_Syntax_Error_Is_Reported()
    {
        var r = LuaGadgetHost.Run("vp.line(0, 0,");

        Assert.False(r.Ok);
        Assert.Contains("syntax", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_Runtime_Error_Is_Reported()
    {
        var r = LuaGadgetHost.Run("vp.circle(0, 0, -5)");

        Assert.False(r.Ok);
        Assert.Empty(r.Shapes);
    }

    [Fact]
    public void A_Script_That_Draws_Nothing_Says_So()
    {
        var r = LuaGadgetHost.Run("local x = 1 + 1");

        Assert.False(r.Ok);
        Assert.Contains("drew nothing", r.Error!);
    }

    [Fact]
    public void An_Empty_Script_Is_Refused()
    {
        Assert.False(LuaGadgetHost.Run("").Ok);
        Assert.False(LuaGadgetHost.Run("   ").Ok);
    }

    [Fact]
    public void A_Polyline_With_One_Point_Is_Refused()
    {
        var r = LuaGadgetHost.Run("vp.polyline({{0,0}}, false)");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Math_Library_Is_Available_But_Io_Is_Not()
    {
        // The sandbox must keep what gadgets need and drop what they must not have.
        Assert.True(LuaGadgetHost.Run("vp.circle(0, 0, math.sqrt(16))").Ok);
        Assert.False(LuaGadgetHost.Run("vp.circle(0, 0, io and 4 or 4) io.write('x')").Ok);
    }
}
