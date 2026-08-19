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
        PopulateStrategies();
        RefreshList();
    }

    private void RefreshList()
    {
        ToolpathList.Items.Clear();
        for (int i = 0; i < AppState.Toolpaths.Toolpaths.Count; i++)
        {
            var tp = AppState.Toolpaths.Toolpaths[i];
            var flag = tp.IsDirty ? " ⚠ dirty" : "";
            ToolpathList.Items.Add($"{tp.Name} [{tp.Strategy}]{flag} — {tp.GCode.Count} lines");
        }
    }

    private Toolpath? _selectedToolpath;

    private void ToolpathList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = ToolpathList.SelectedIndex;
        _selectedToolpath = index >= 0 && index < AppState.Toolpaths.Toolpaths.Count
            ? AppState.Toolpaths.Toolpaths[index]
            : null;
        RefreshParamsForm(_selectedToolpath);
        PublishSourceLink(_selectedToolpath);
    }

    /// <summary>
    /// Card P2: publish which shapes this toolpath was calculated from, so the
    /// Design canvas can highlight them. SelectedShapeIds already existed; nothing
    /// ever surfaced it, so a user could not tell what a toolpath actually cuts.
    /// </summary>
    private void PublishSourceLink(Toolpath? tp)
    {
        AppState.FollowedSourceShapeIds.Clear();
        if (tp is null)
        {
            SourceLinkLabel.Text = "";
            return;
        }

        foreach (var id in tp.SelectedShapeIds) AppState.FollowedSourceShapeIds.Add(id);

        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        int present = layer?.Shapes.Count(s => tp.SelectedShapeIds.Contains(s.Id)) ?? 0;
        int missing = tp.SelectedShapeIds.Count - present;

        SourceLinkLabel.Text = tp.SelectedShapeIds.Count == 0
            ? "no source shapes linked"
            : missing > 0
                ? $"cuts {present} shape(s) — {missing} source shape(s) deleted, recalculate"
                : $"cuts {present} shape(s)";
        SourceLinkLabel.Foreground = missing > 0
            ? System.Windows.Media.Brushes.Firebrick
            : System.Windows.Media.Brushes.DimGray;

        AppState.RaiseFollowedSourceChanged();
    }

    private sealed class ParamRow
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>Build the params form from the selected toolpath's ParamsJson
    /// (one expression-enabled text box per numeric parameter, SPK-0209).</summary>
    private void RefreshParamsForm(Toolpath? tp)
    {
        ParamsGrid.ItemsSource = null;
        if (tp is null) return;
        try
        {
            var obj = System.Text.Json.Nodes.JsonNode.Parse(tp.ParamsJson)?.AsObject();
            if (obj is null) return;
            var rows = new List<ParamRow>();
            foreach (var kv in obj)
            {
                if (kv.Value is not null && kv.Value.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                {
                    rows.Add(new ParamRow { Key = kv.Key, Text = kv.Value.ToString() });
                }
            }
            ParamsGrid.ItemsSource = rows;
        }
        catch
        {
            ParamsGrid.ItemsSource = null;
        }
    }

    /// <summary>Resolve every params-row expression and write the result back
    /// into tp.ParamsJson (plain numbers pass through; "2440/2" and "$width/2"
    /// evaluate; unresolved expressions fall back to a numeric parse).</summary>
    private static void CommitParamsForm(Toolpath tp)
    {
        try
        {
            var obj = System.Text.Json.Nodes.JsonNode.Parse(tp.ParamsJson)?.AsObject();
            if (obj is null) return;
            var variables = LoadDocumentVariables();
            foreach (var kv in obj)
            {
                var raw = kv.Value?.ToString();
                if (raw is null) continue;
                var resolved = ExpressionCalculator.Evaluate(raw, variables);
                if (resolved is { } r)
                {
                    obj[kv.Key] = r;
                }
                else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    obj[kv.Key] = d;
                }
            }
            tp.ParamsJson = obj.ToJsonString();
        }
        catch
        {
            // Leave ParamsJson untouched on any parse failure.
        }
    }

    private static List<DocumentVariable> LoadDocumentVariables()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VectorPilot", "document-variables.json");
            if (!File.Exists(path)) return new List<DocumentVariable>();
            return System.Text.Json.JsonSerializer.Deserialize<List<DocumentVariable>>(File.ReadAllText(path)) ?? new List<DocumentVariable>();
        }
        catch
        {
            return new List<DocumentVariable>();
        }
    }

    private static readonly StrategyRegistry Registry = new();

    /// <summary>The registry entry the user has selected in the combo.</summary>
    private StrategyRegistry.Entry? SelectedEntry => CmbStrategy.SelectedItem as StrategyRegistry.Entry;

    private ToolpathStrategy SelectedStrategy
        => SelectedEntry is { } e ? StrategyKeyMap.ToStrategy(e.Key) : ToolpathStrategy.Profile;

    /// <summary>
    /// Resolve the registry entry for a toolpath. Prefers the exact key stored on the
    /// toolpath; falls back to the enum mapping for documents saved before keys existed.
    /// </summary>
    private StrategyRegistry.Entry? EntryFor(Toolpath tp)
        => (tp.StrategyKey is { Length: > 0 } k ? Registry.Find(k) : null)
           ?? Registry.Find(StrategyKeyMap.ToKey(tp.Strategy));

    /// <summary>Fill the combo from the registry so every strategy is selectable.</summary>
    private void PopulateStrategies()
    {
        CmbStrategy.ItemsSource = Registry.Entries;
        if (Registry.Entries.Count > 0) CmbStrategy.SelectedIndex = 0;
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
        // Store the EXACT registry key: the enum is coarser than the registry.
        tp.StrategyKey = SelectedEntry?.Key;
        if (SelectedEntry is { } entry)
        {
            tp.Name = $"{entry.DisplayName} {AppState.Toolpaths.Toolpaths.Count}";
            tp.ParamsJson = entry.DefaultsJson;
        }
        else tp.ParamsJson = "{}";
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
        SetCalcNote("");
        foreach (var tp in AppState.Toolpaths.Toolpaths) RecalculateToolpath(tp);
        RefreshList();
        GCodePreview.Text = string.Join("\n", AppState.Toolpaths.Toolpaths.SelectMany(t => t.GCode).Take(60));
    }

    private void RecalculateToolpath(Toolpath tp)
    {
        // Commit the params form (expression resolution) before dispatch.
        CommitParamsForm(tp);
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) return;
        var shapes = layer.Shapes.Where(s => tp.SelectedShapeIds.Contains(s.Id)).ToList();
        if (shapes.Count == 0)
        {
            SetCalcNote($"{tp.Name}: no source shapes — select geometry in Design.");
            return;
        }

        var entry = EntryFor(tp);
        if (entry is null)
        {
            // No silent profile substitution: say what happened instead of cutting
            // something the user did not ask for.
            tp.GCode.Clear();
            tp.GCode.Add($"(VectorPilot: no strategy registered for '{tp.StrategyKey ?? tp.Strategy.ToString()}')");
            tp.IsDirty = true;
            SetCalcNote($"{tp.Name}: strategy '{tp.StrategyKey ?? tp.Strategy.ToString()}' is not registered — nothing calculated.");
            return;
        }

        if (entry.UsesHeightfield && AppState.Heightfield is null)
        {
            tp.GCode.Clear();
            tp.GCode.Add($"({entry.DisplayName}: needs a 3D relief — bake one in the Model stage)");
            tp.IsDirty = true;
            SetCalcNote($"{entry.DisplayName} needs a relief. Build one in the Model stage, then Calculate.");
            return;
        }

        var result = entry.Compute(shapes, AppState.Heightfield, tp.ParamsJson);
        if (result.Gcode.Count == 0)
        {
            tp.GCode.Clear();
            tp.GCode.Add($"({entry.DisplayName}: produced no moves for the current selection)");
            tp.IsDirty = true;
            SetCalcNote($"{entry.DisplayName} produced no moves — check the parameters or selection.");
            return;
        }

        var header = new List<string> { $"(VectorPilot {entry.DisplayName} — {tp.Name})" };
        header.AddRange(result.Gcode);
        tp.GCode.Clear();
        tp.GCode.AddRange(header);
        tp.EstimatedTimeSeconds = result.EstimatedTimeSeconds;
        tp.IsDirty = false;
    }

    /// <summary>Surface why Calculate produced nothing, instead of failing silently.</summary>
    private void SetCalcNote(string message)
    {
        CalcNote.Text = message;
        CalcNote.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        var sorted = ToolpathSorter.Sort(AppState.Toolpaths.Toolpaths, ToolpathSortMode.ByTool);
        AppState.Toolpaths.Toolpaths.Clear();
        AppState.Toolpaths.Toolpaths.AddRange(sorted);
        RefreshList();
    }

    private void RecalcDirty_Click(object sender, RoutedEventArgs e)
    {
        var all = AppState.Toolpaths.Toolpaths;
        if (all.Count == 0)
        {
            MessageBox.Show("Add a toolpath first.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dirty = new DirtyRegionManager();
        foreach (var tp in all)
        {
            if (tp.IsDirty) dirty.MarkFullTreeDirty();
        }
        if (!dirty.NeedsResimulation)
        {
            MessageBox.Show("Nothing to recalculate — no dirty toolpaths.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var tp in ToolpathResimPlanner.AffectedToolpaths(all, dirty)) RecalculateToolpath(tp);
        dirty.Clear();
        RefreshList();
        GCodePreview.Text = string.Join("\n", all.SelectMany(t => t.GCode).Take(60));
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
