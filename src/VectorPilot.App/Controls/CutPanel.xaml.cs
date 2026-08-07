using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class CutPanel : UserControl
{
    public CutPanel()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        ToolpathList.Items.Clear();
        foreach (var tp in AppState.Toolpaths.Toolpaths)
        {
            var flag = tp.IsDirty ? " ⚠ dirty" : "";
            ToolpathList.Items.Add($"{tp.Name} [{tp.Strategy}]{flag} — {tp.GCode.Count} lines");
        }
    }

    private ToolpathStrategy SelectedStrategy
    {
        get
        {
            var item = CmbStrategy.SelectedItem as ComboBoxItem;
            var label = item?.Content as string;
            return label switch
            {
                "Pocket" => ToolpathStrategy.Pocket,
                "V-Carve" => ToolpathStrategy.VCarve,
                "Drill" => ToolpathStrategy.Drill,
                _ => ToolpathStrategy.Profile
            };
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null || layer.Shapes.Count == 0)
        {
            MessageBox.Show("Draw at least one shape in the Design stage first.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        double depth = ParseOr(TxtDepth.Text, 0.25);
        double feed = ParseOr(TxtFeed.Text, 100);
        var tp = AppState.Toolpaths.Add(SelectedStrategy);
        tp.CutDepth = depth;
        tp.FeedRate = feed;
        foreach (var s in layer.Shapes) tp.SelectedShapeIds.Add(s.Id);
        RefreshList();
    }

    private void BtnCalc_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.Toolpaths.Toolpaths.Count == 0)
        {
            MessageBox.Show("Add a toolpath first.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var tp in AppState.Toolpaths.Toolpaths)
        {
            var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
            if (layer is null) continue;
            var shapes = layer.Shapes.Where(s => tp.SelectedShapeIds.Contains(s.Id)).ToList();
            if (shapes.Count == 0) continue;

            var g = new List<string> { $"(VectorPilot {tp.Strategy} — {tp.Name})" };
            foreach (var shape in shapes)
            {
                g.AddRange(ToolpathGenerator.GenerateProfile(shape, tp.CutDepth, 0.2, tp.FeedRate, tp.FeedRate * 0.5, 12000));
            }
            tp.GCode.Clear();
            tp.GCode.AddRange(g);
            tp.IsDirty = false;
        }
        RefreshList();
        GCodePreview.Text = string.Join("\n", AppState.Toolpaths.Toolpaths.SelectMany(t => t.GCode).Take(60));
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var all = AppState.Toolpaths.Toolpaths.SelectMany(t => t.GCode).ToList();
        if (all.Count == 0)
        {
            MessageBox.Show("Calculate at least one toolpath first.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AppState.LoadedGCode = all;
        AppState.LoadedGCodePath = null;
        MessageBox.Show($"{all.Count} lines of G-code handed to the Machine stage.\nGo to Machine → Connect (Simulator) → Load → Start.", "VectorPilot");
    }

    private static double ParseOr(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
