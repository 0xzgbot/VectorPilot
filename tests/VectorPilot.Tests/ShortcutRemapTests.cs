using VectorPilot.App;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Keyboard shortcut remapping (Mac SPK-UXPOLISH parity). The important property
/// is that a gesture can never be bound to two commands — silent shadowing would
/// make a shortcut stop working with no explanation.
/// </summary>
public class ShortcutRemapTests
{
    [Fact]
    public void Defaults_Are_Available()
    {
        var s = new ShortcutStore();
        Assert.Equal("Ctrl+Z", s.Gesture("undo"));
        Assert.Equal("Ctrl+G", s.Gesture("group"));
        Assert.Equal("Ctrl+Shift+G", s.Gesture("ungroup"));
        Assert.False(s.IsRemapped("undo"));
    }

    [Fact]
    public void Unknown_Commands_Have_No_Gesture()
    {
        var s = new ShortcutStore();
        Assert.Null(s.Gesture("no-such-command"));
        Assert.False(s.Remap("no-such-command", "Ctrl+Q"));
    }

    [Fact]
    public void Remapping_Changes_The_Effective_Gesture()
    {
        var s = new ShortcutStore();
        Assert.True(s.Remap("duplicate", "Ctrl+Alt+D"));

        Assert.Equal("Ctrl+Alt+D", s.Gesture("duplicate"));
        Assert.Equal("Ctrl+D", s.DefaultGesture("duplicate"));   // default is remembered
        Assert.True(s.IsRemapped("duplicate"));
    }

    [Fact]
    public void A_Gesture_Cannot_Be_Bound_Twice()
    {
        var s = new ShortcutStore();
        // Ctrl+Z already belongs to undo.
        Assert.False(s.Remap("duplicate", "Ctrl+Z"));
        Assert.Equal("Ctrl+D", s.Gesture("duplicate"));   // unchanged
    }

    [Fact]
    public void Rebinding_A_Command_To_Its_Own_Gesture_Is_Allowed()
    {
        var s = new ShortcutStore();
        Assert.True(s.Remap("undo", "Ctrl+Z"));
        Assert.False(s.IsRemapped("undo"));   // same as default: no override stored
    }

    [Fact]
    public void Returning_To_The_Default_Drops_The_Override()
    {
        var s = new ShortcutStore();
        s.Remap("group", "Ctrl+Alt+G");
        Assert.True(s.IsRemapped("group"));

        s.Remap("group", "Ctrl+G");
        Assert.False(s.IsRemapped("group"));
    }

    [Fact]
    public void CommandFor_Finds_The_Owner_Of_A_Gesture()
    {
        var s = new ShortcutStore();
        Assert.Equal("undo", s.CommandFor("Ctrl+Z"));
        Assert.Equal("undo", s.CommandFor("ctrl+z"));   // case-insensitive
        Assert.Null(s.CommandFor("Ctrl+F12"));
    }

    [Fact]
    public void Empty_Gestures_Are_Rejected()
    {
        var s = new ShortcutStore();
        Assert.False(s.Remap("undo", ""));
        Assert.False(s.Remap("undo", "   "));
    }

    [Fact]
    public void Reset_Restores_A_Single_Command()
    {
        var s = new ShortcutStore();
        s.Remap("save", "Ctrl+Alt+S");
        Assert.True(s.ResetCommand("save"));
        Assert.Equal("Ctrl+S", s.Gesture("save"));
        Assert.False(s.ResetCommand("save"));   // nothing left to reset
    }

    [Fact]
    public void ResetAll_Restores_Every_Default()
    {
        var s = new ShortcutStore();
        s.Remap("save", "Ctrl+Alt+S");
        s.Remap("open", "Ctrl+Alt+O");

        s.ResetAll();

        Assert.Equal("Ctrl+S", s.Gesture("save"));
        Assert.Equal("Ctrl+O", s.Gesture("open"));
        Assert.All(s.CommandIds, id => Assert.False(s.IsRemapped(id)));
    }

    [Fact]
    public void Overrides_Round_Trip_Through_Json()
    {
        var a = new ShortcutStore();
        a.Remap("palette", "F1");
        a.Remap("fit-view", "Ctrl+Alt+0");

        var b = new ShortcutStore();
        b.LoadJson(a.ToJson());

        Assert.Equal("F1", b.Gesture("palette"));
        Assert.Equal("Ctrl+Alt+0", b.Gesture("fit-view"));
        Assert.Equal("Ctrl+Z", b.Gesture("undo"));   // untouched command keeps its default
    }

    [Fact]
    public void A_Corrupt_File_Falls_Back_To_Defaults()
    {
        var s = new ShortcutStore();
        s.LoadJson("{ this is not json");
        Assert.Equal("Ctrl+Z", s.Gesture("undo"));
    }

    [Fact]
    public void Json_Cannot_Introduce_A_Conflict()
    {
        // A hand-edited file binding two commands to Ctrl+Z must not shadow undo.
        var s = new ShortcutStore();
        s.LoadJson("{\"duplicate\":\"Ctrl+Z\"}");

        Assert.Equal("Ctrl+Z", s.Gesture("undo"));
        Assert.Equal("Ctrl+D", s.Gesture("duplicate"));   // rejected on load
    }
}
