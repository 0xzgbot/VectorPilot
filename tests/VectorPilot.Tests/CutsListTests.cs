using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-102: the LightBurn-style Cuts list.
///
/// The toolpath list was a ListBox of formatted STRINGS. Six call-sites in CutPanel
/// (ArrayCopy_Click, the array and merge result selection, the StrategyKey lookup,
/// SaveTemplate_Click, ApplyTemplate_Click) all test `SelectedItem is not Toolpath`,
/// so with strings in the list every one of those guards failed and Array copy,
/// Save template and Apply template were dead buttons that only printed a refusal.
///
/// These tests drive the real control, so a green test cannot coexist with a list
/// that holds strings again.
/// </summary>
[Collection("STA")]
public class CutsListTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                var res = Application.Current!.Resources;
                if (!res.Contains("PanelBg"))
                {
                    res["RailBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x19, 0x19, 0x22));
                    res["RailHover"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x3A));
                    res["Accent"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
                    res["PanelBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF4, 0xF4, 0xF6));
                    res["TextOnDark"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xF0));
                    res["RailButton"] = new Style(typeof(Button));
                }
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw error;
    }

    /// <summary>Fresh toolpath tree, then a panel bound to it.</summary>
    private static CutPanel PanelWith(params Toolpath[] toolpaths)
    {
        AppState.Toolpaths.Toolpaths.Clear();
        foreach (var tp in toolpaths) AppState.Toolpaths.Toolpaths.Add(tp);
        var panel = new CutPanel();
        panel.RefreshCutsList();
        return panel;
    }

    private static ListView List(CutPanel panel) => (ListView)panel.FindName("ToolpathList")!;

    private static Toolpath Tp(string name, string key = "profile", int lines = 0,
                               bool dirty = false, double seconds = 0,
                               string paramsJson = "{}")
    {
        var tp = new Toolpath
        {
            Name = name,
            StrategyKey = key,
            IsDirty = dirty,
            EstimatedTimeSeconds = seconds,
            ParamsJson = paramsJson,
        };
        for (int i = 0; i < lines; i++) tp.GCode.Add($"G1 X{i}");
        return tp;
    }

    // ---- the list holds objects, not strings ----

    [Fact]
    public void RefreshList_Adds_Toolpath_Objects_Not_Strings()
    {
        OnSta(() =>
        {
            var panel = PanelWith(Tp("Profile 0"), Tp("Pocket 1", "pocket"));
            var list = List(panel);

            Assert.Equal(2, list.Items.Count);
            foreach (var item in list.Items)
            {
                Assert.IsType<Toolpath>(item);
                Assert.IsNotType<string>(item);
            }
        });
    }

    [Fact]
    public void SelectedItem_Is_A_Toolpath_So_The_Guards_Pass()
    {
        // This is the bug: `if (ToolpathList.SelectedItem is not Toolpath tp) return;`
        // in ArrayCopy_Click / SaveTemplate_Click / ApplyTemplate_Click.
        OnSta(() =>
        {
            var panel = PanelWith(Tp("Profile 0"), Tp("Pocket 1", "pocket"));
            var list = List(panel);

            list.SelectedIndex = 1;

            Assert.IsType<Toolpath>(list.SelectedItem);
            Assert.False(list.SelectedItem is not Toolpath, "the `is not Toolpath` guard still fails");
            Assert.Equal("Pocket 1", ((Toolpath)list.SelectedItem!).Name);
            Assert.Equal("pocket", ((Toolpath)list.SelectedItem!).StrategyKey);
        });
    }

    [Fact]
    public void Assigning_SelectedItem_To_A_Toolpath_Selects_That_Row()
    {
        // The array and merge results do `ToolpathList.SelectedItem = tp;`.
        OnSta(() =>
        {
            var wanted = Tp("Pocket 1", "pocket");
            var panel = PanelWith(Tp("Profile 0"), wanted);
            var list = List(panel);

            list.SelectedItem = wanted;

            Assert.Equal(1, list.SelectedIndex);
            Assert.Same(wanted, list.SelectedItem);
        });
    }

    // ---- selecting a row drives the params form for THAT toolpath ----

    [Fact]
    public void Selecting_A_Row_Loads_That_Toolpaths_Params()
    {
        OnSta(() =>
        {
            var panel = PanelWith(
                Tp("Profile 0", paramsJson: """{"cutDepthMm":6}"""),
                Tp("Pocket 1", "pocket", paramsJson: """{"stepoverPct":45}"""));
            var list = List(panel);
            var grid = (ItemsControl)panel.FindName("ParamsGrid")!;

            list.SelectedIndex = 0;
            Assert.Contains("cutDepthMm", ParamKeys(grid));

            list.SelectedIndex = 1;
            var keys = ParamKeys(grid);
            Assert.Contains("stepoverPct", keys);
            Assert.DoesNotContain("cutDepthMm", keys);
        });
    }

    private static List<string> ParamKeys(ItemsControl grid)
    {
        var keys = new List<string>();
        if (grid.ItemsSource is null) return keys;
        foreach (var row in grid.ItemsSource)
        {
            var prop = row.GetType().GetProperty("Key");
            if (prop?.GetValue(row) is string k) keys.Add(k);
        }
        return keys;
    }

    // ---- the columns ----

    [Fact]
    public void Time_Column_Shows_An_Estimate_Once_It_Is_Set()
    {
        var conv = new ToolpathTimeConverter();
        var tp = Tp("Profile 0", seconds: 125);

        var shown = (string)conv.Convert(tp, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.False(string.IsNullOrWhiteSpace(shown), "time column was blank for a real estimate");
        Assert.Equal("2:05", shown);
    }

    [Fact]
    public void A_Zero_Time_Renders_As_The_Placeholder()
    {
        var conv = new ToolpathTimeConverter();

        Assert.Equal("—", (string)conv.Convert(Tp("Profile 0"), typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("—", CutPanel.TimeLabel(0));
        Assert.NotEqual("—", CutPanel.TimeLabel(1));
    }

    [Fact]
    public void Dirty_Column_Marks_Only_The_Dirty_Toolpath()
    {
        var conv = new ToolpathDirtyConverter();

        var dirty = (string)conv.Convert(Tp("A", dirty: true), typeof(string), null!, CultureInfo.InvariantCulture);
        var clean = (string)conv.Convert(Tp("B"), typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.False(string.IsNullOrEmpty(dirty), "a dirty toolpath showed no marker");
        Assert.Equal("", clean);
    }

    [Fact]
    public void Strategy_Column_Prefers_The_Registry_Key()
    {
        var conv = new ToolpathStrategyConverter();
        var keyed = Tp("A", "photo-vcarve");
        var unkeyed = Tp("B", "");

        Assert.Equal("photo-vcarve",
            (string)conv.Convert(keyed, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal(unkeyed.Strategy.ToString(),
            (string)conv.Convert(unkeyed, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Lines_Column_Reads_The_GCode_Count()
    {
        OnSta(() =>
        {
            var tp = Tp("Profile 0", lines: 17);
            var panel = PanelWith(tp);
            var list = List(panel);

            var item = Assert.IsType<Toolpath>(list.Items[0]);
            Assert.Equal(17, item.GCode.Count);
            Assert.Same(tp, item);
        });
    }

    // ---- refresh behaviour ----

    [Fact]
    public void Selection_Survives_A_RefreshList()
    {
        OnSta(() =>
        {
            var wanted = Tp("Pocket 1", "pocket");
            var panel = PanelWith(Tp("Profile 0"), wanted, Tp("Drill 2", "drill"));
            var list = List(panel);
            list.SelectedItem = wanted;

            panel.RefreshCutsList();

            Assert.Same(wanted, list.SelectedItem);
            Assert.Equal(1, list.SelectedIndex);
        });
    }

    [Fact]
    public void An_Empty_Toolpath_List_Renders_Empty_Without_Crashing()
    {
        OnSta(() =>
        {
            var panel = PanelWith();
            var list = List(panel);

            Assert.Empty(list.Items);
            Assert.Null(list.SelectedItem);

            panel.RefreshCutsList();
            Assert.Empty(list.Items);
        });
    }
}
