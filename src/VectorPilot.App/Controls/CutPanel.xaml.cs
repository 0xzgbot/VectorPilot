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
        RefreshTemplates();
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
        RefreshTemplates();
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

        // Keep-out zones apply to whatever is cut here; say so on the Cut stage rather
        // than only drawing them on the Design canvas.
        int activeZones = AppState.CurrentJob?.KeepOutZones.Count(z => z.IsActive) ?? 0;
        if (activeZones > 0)
            SourceLinkLabel.Text += $"  ·  {activeZones} keep-out zone(s) active";
        SourceLinkLabel.Foreground = missing > 0
            ? System.Windows.Media.Brushes.Firebrick
            : System.Windows.Media.Brushes.DimGray;

        AppState.RaiseFollowedSourceChanged();
    }

    internal sealed class ParamRow
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>Enum option names; empty for free-text/numeric params.</summary>
        public List<string> Choices { get; set; } = new();

        public bool HasChoices => Choices.Count > 0;
        public bool IsFreeText => Choices.Count == 0;
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

            var enums = EnumChoicesFor(tp);
            var rows = new List<ParamRow>();
            foreach (var kv in obj)
            {
                if (kv.Value is null) continue;
                var kind = kv.Value.GetValueKind();

                // Numbers, bools and enum-backed ints are all editable. Previously only
                // Number rows rendered, so weave's pattern and moulding's profile — the
                // fields that decide what gets cut — were invisible and unreachable.
                if (kind is System.Text.Json.JsonValueKind.Number
                         or System.Text.Json.JsonValueKind.True
                         or System.Text.Json.JsonValueKind.False)
                {
                    var row = new ParamRow { Key = kv.Key, Text = kv.Value.ToString() };
                    if (enums.TryGetValue(kv.Key, out var names))
                    {
                        row.Choices = names;
                        // Enums serialize as ints; show the NAME so the dropdown has a
                        // matching selection instead of appearing blank.
                        if (int.TryParse(row.Text, out int idx) && idx >= 0 && idx < names.Count)
                            row.Text = names[idx];
                    }
                    rows.Add(row);
                }
            }
            ParamsGrid.ItemsSource = rows;
        }
        catch
        {
            ParamsGrid.ItemsSource = null;
        }
    }

    /// <summary>
    /// Enum-valued params for a strategy, as name lists indexed by JSON property.
    /// Discovered by reflection over the params type so no strategy needs a bespoke form.
    /// </summary>
    private Dictionary<string, List<string>> EnumChoicesFor(Toolpath tp)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var entry = EntryFor(tp);
        if (entry is null) return map;

        var type = Registry.ParamsTypeFor(entry.Key);
        if (type is null) return map;

        foreach (var prop in type.GetProperties())
        {
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (!t.IsEnum) continue;
            // JSON uses camelCase property names.
            string json = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
            map[json] = Enum.GetNames(t).ToList();
        }
        return map;
    }

    /// <summary>
    /// Merge the params form's edited rows into tp.ParamsJson, resolving expressions
    /// ("2440/2", "$width/2"), booleans and enum names.
    ///
    /// This used to re-parse ParamsJson and write it straight back without ever reading
    /// ParamsGrid — so every value the user typed was discarded and Calculate always ran
    /// the strategy defaults.
    /// </summary>
    private void CommitParamsForm(Toolpath tp)
    {
        try
        {
            var obj = System.Text.Json.Nodes.JsonNode.Parse(tp.ParamsJson)?.AsObject();
            if (obj is null) return;

            var edits = (ParamsGrid.ItemsSource as IEnumerable<ParamRow>)?
                .ToDictionary(r => r.Key, r => r.Text, StringComparer.OrdinalIgnoreCase);

            var variables = LoadDocumentVariables();
            foreach (var key in obj.Select(k => k.Key).ToList())
            {
                // Prefer what the user typed; fall back to the stored value.
                string? raw = edits is not null && edits.TryGetValue(key, out var typed)
                    ? typed
                    : obj[key]?.ToString();
                if (raw is null) continue;
                raw = raw.Trim();

                if (bool.TryParse(raw, out bool b)) { obj[key] = b; continue; }

                var resolved = ExpressionCalculator.Evaluate(raw, variables);
                if (resolved is { } r) { obj[key] = r; continue; }

                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    obj[key] = d;
                    continue;
                }

                // Enum typed by name ("Twill") — store the underlying int the
                // strategy's params type expects.
                if (EnumValueFor(tp, key, raw) is { } enumValue) obj[key] = enumValue;
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

    /// <summary>Underlying int for an enum param typed by name, or null.</summary>
    private int? EnumValueFor(Toolpath tp, string jsonKey, string text)
    {
        var entry = EntryFor(tp);
        if (entry is null) return null;
        var type = Registry.ParamsTypeFor(entry.Key);
        if (type is null) return null;

        var prop = type.GetProperties().FirstOrDefault(p =>
            string.Equals(char.ToLowerInvariant(p.Name[0]) + p.Name[1..], jsonKey,
                          StringComparison.OrdinalIgnoreCase));
        if (prop is null) return null;

        var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (!t.IsEnum) return null;

        return Enum.TryParse(t, text, ignoreCase: true, out var parsed)
            ? Convert.ToInt32(parsed)
            : null;
    }

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
        // Select the new toolpath so its params form is immediately editable.
        // Without this the user adds a strategy and the Params row stays blank.
        ToolpathList.SelectedIndex = AppState.Toolpaths.Toolpaths.Count - 1;
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

    /// <summary>
    /// Repeat the selected toolpath's G-code as a NEW toolpath. ArrayCopyEngine shipped
    /// with zero app call-sites, so a user could not array anything.
    /// </summary>
    private void ArrayCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ToolpathList.SelectedItem is not Toolpath src)
        {
            SetCalcNote("Select a calculated toolpath to array.");
            return;
        }
        if (src.GCode.Count == 0)
        {
            SetCalcNote($"{src.Name} has no G-code yet — Calculate first.");
            return;
        }

        int count = int.TryParse(TxtArrayCount.Text, out var c) ? c : 3;
        double spacing = double.TryParse(TxtArraySpacing.Text, out var s) ? s : 50;
        int rows = int.TryParse(TxtArrayRows.Text, out var r) ? r : 2;

        if (count < 2) { SetCalcNote("An array needs a count of 2 or more."); return; }

        string kind = (CmbArrayType.SelectedItem as ComboBoxItem)?.Content as string ?? "Linear";

        var result = kind switch
        {
            "Grid" => ArrayCopyEngine.ComputeGrid(src.GCode,
                new GridPattern { Columns = count, Rows = rows, ColumnSpacingMm = spacing, RowSpacingMm = spacing }),
            "Circular" => ArrayCopyEngine.ComputeCircular(src.GCode,
                new CircularPattern { Count = count, RadiusMm = spacing, CenterX = 0, CenterY = 0 }),
            _ => ArrayCopyEngine.ComputeLinear(src.GCode,
                new LinearPattern { Count = count, SpacingMm = spacing })
        };

        if (!result.Success || result.GcodeLines.Count == 0)
        {
            SetCalcNote(result.ErrorMessage ?? "The array produced no moves.");
            return;
        }

        // A NEW toolpath: the original is never modified, so "undo" is simply deleting
        // this one.
        var tp = AppState.Toolpaths.Add(src.Strategy);
        tp.Name = $"{src.Name} — {kind} x{result.TotalCount}";
        tp.StrategyKey = src.StrategyKey;
        tp.ParamsJson = src.ParamsJson;
        tp.CutDepth = src.CutDepth;
        tp.FeedRate = src.FeedRate;
        foreach (var id in src.SelectedShapeIds) tp.SelectedShapeIds.Add(id);
        tp.GCode.Clear();
        tp.GCode.AddRange(result.GcodeLines);
        tp.IsDirty = false;

        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        RefreshList();
        ToolpathList.SelectedItem = tp;
        SetCalcNote($"Added \"{tp.Name}\" — {result.TotalCount} instances, original kept.");
    }

    // ---- toolpath templates (ToolpathTemplateManager had zero app call-sites) ----

    private static ToolpathTemplateManager? _templates;

    private static ToolpathTemplateManager Templates => _templates ??= new ToolpathTemplateManager(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorPilot", "toolpath-templates.json"));

    /// <summary>Repopulate the template combo for whichever strategy is in play.</summary>
    private void RefreshTemplates()
    {
        string key = (ToolpathList.SelectedItem as Toolpath)?.StrategyKey
                     ?? SelectedEntry?.Key
                     ?? "profile";

        CmbTemplate.ItemsSource = Templates.ForStrategy(key);
        if (CmbTemplate.Items.Count > 0) CmbTemplate.SelectedIndex = 0;
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (ToolpathList.SelectedItem is not Toolpath tp)
        {
            SetCalcNote("Select a toolpath whose parameters you want to save.");
            return;
        }

        // Commit any pending edits in the params form first, so the template captures
        // what the user actually typed.
        CommitParamsForm(tp);

        var dlg = new TemplateNameDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.TemplateName)) return;

        string key = string.IsNullOrEmpty(tp.StrategyKey)
            ? StrategyKeyMap.ToKey(tp.Strategy)
            : tp.StrategyKey;

        var type = key switch
        {
            "pocket" => ToolpathTemplateType.Pocket,
            "drill" or "drill-bank" => ToolpathTemplateType.Drill,
            "vcarve" => ToolpathTemplateType.VCarve,
            "quick-engrave" => ToolpathTemplateType.QuickEngrave,
            _ => ToolpathTemplateType.Profile
        };

        var saved = Templates.SaveTemplate(dlg.TemplateName.Trim(), type, tp.ParamsJson, key);
        RefreshTemplates();
        CmbTemplate.SelectedItem = Templates.Templates.FirstOrDefault(t => t.Id == saved.Id);
        SetCalcNote($"Saved template \"{saved.Name}\" for {key}.");
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTemplate.SelectedItem is not ToolpathTemplate template)
        {
            SetCalcNote("No template selected — use \"Save as…\" to create one.");
            return;
        }
        if (ToolpathList.SelectedItem is not Toolpath tp)
        {
            SetCalcNote("Select the toolpath to apply the template to.");
            return;
        }

        // Applying changes ParamsJson BEFORE Calculate, so the next Calculate uses it.
        tp.ParamsJson = template.ParamsJson;
        tp.IsDirty = true;

        RefreshParamsForm(tp);
        RefreshList();
        SetCalcNote($"Applied \"{template.Name}\" to {tp.Name} — Calculate to regenerate.");
    }

    /// <summary>
    /// Why an area strategy cannot run on this selection, or null if it can.
    ///
    /// Profile/Pocket/V-Carve are area operations: an OPEN outline has no inside, so
    /// cutting one produces junk. VectorValidator existed but nothing called it, so this
    /// Internal so tests exercise the same decision the UI makes.
    /// </summary>
    public static string? AreaStrategyBlocker(string strategyKey, string displayName, IReadOnlyList<VectorShape> shapes)
    {
        if (strategyKey is not ("profile" or "pocket" or "vcarve")) return null;
        if (shapes.Count == 0) return null;

        static bool IsOpen(VectorShape s)
            => !s.Closed && s.Type != ShapeType.Circle && s.Type != ShapeType.Rectangle;

        int open = shapes.Count(IsOpen);
        if (open < shapes.Count) return null;   // at least one closed shape to cut

        return $"{displayName} needs a closed outline — {open} selected shape(s) are open paths. " +
               "Close them, or use Extend in Design to make the ends meet.";
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

        // Profile and Pocket are area operations: an OPEN outline has no inside, so
        // cutting one produces junk (a pocket "fills" a region that is not enclosed).
        // VectorValidator existed but nothing called it, so this reached the machine.
        if (AreaStrategyBlocker(entry.Key, entry.DisplayName, shapes) is { } blocker)
        {
            tp.GCode.Clear();
            tp.GCode.Add($"({entry.DisplayName}: {blocker})");
            tp.IsDirty = true;
            SetCalcNote(blocker);
            return;
        }

        var result = entry.Compute(shapes, AppState.Heightfield, tp.ParamsJson);
        if (result.Gcode.Count == 0)
        {
            // Prefer the strategy's own reason ("needs a 3D model…") over a generic
            // message, and never leave a runnable-looking stub behind.
            string why = result.Error
                ?? $"{entry.DisplayName} produced no moves — check the parameters or selection.";

            tp.GCode.Clear();
            tp.GCode.Add($"({entry.DisplayName}: {why})");
            tp.IsDirty = true;
            SetCalcNote(why);
            return;
        }

        var header = new List<string> { $"(VectorPilot {entry.DisplayName} — {tp.Name})" };
        header.AddRange(result.Gcode);
        tp.GCode.Clear();
        tp.GCode.AddRange(header);
        tp.EstimatedTimeSeconds = result.EstimatedTimeSeconds;
        tp.IsDirty = false;

        // Keep-out zones are a physical-safety feature: the engine rule existed but the
        // Cut stage never called it, so a toolpath could cut straight through a clamp
        // with no warning anywhere in the UI.
        var zones = AppState.CurrentJob?.KeepOutZones;
        if (zones is { Count: > 0 } &&
            ToolpathPreflight.KeepOutZoneViolation(tp.Name, zones, tp.GCode, tp.Id) is { } issue)
        {
            SetCalcNote(issue.Message);
        }
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
