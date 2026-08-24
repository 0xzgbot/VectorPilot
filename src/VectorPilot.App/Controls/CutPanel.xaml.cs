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
        PopulatePresets();   // H-501
        RefreshList();
        RefreshTemplates();
    }

    // ---- H-501: material + bit presets fill the cut params ----

    private ToolDatabase? _toolDb;
    private MaterialDatabase? _materialDb;
    private bool _loadingPresets;

    /// <summary>Load (or reuse) the persisted tool + material databases.</summary>
    private void EnsureDatabases()
    {
        if (_toolDb is null)
        {
            var toolPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VectorPilot", "tools.json");
            _toolDb = File.Exists(toolPath)
                ? ToolDatabase.LoadFromJson(toolPath)
                : new ToolDatabase(seedDefaults: true);
            _toolDbPath = toolPath;
        }
        if (_materialDb is null)
        {
            var matPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VectorPilot", "materials.json");
            _materialDb = new MaterialDatabase(matPath).WithDefaults();
        }
    }

    private string? _toolDbPath;
    private string ToolDbPath => _toolDbPath ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VectorPilot", "tools.json");

    private void PopulatePresets()
    {
        _loadingPresets = true;
        try
        {
            EnsureDatabases();

            // Materials first — the preset list the operator actually reasons about.
            CmbMaterialPreset.ItemsSource = _materialDb!.Materials.Select(m => m.Name).ToList();
            if (_materialDb.Materials.Count > 0) CmbMaterialPreset.SelectedIndex = 0;

            CmbToolPreset.ItemsSource = _toolDb!.Tools
                .OrderBy(t => t.DiameterMm)
                .Select(t => $"{t.Name}  ⌀{t.DiameterMm:0.##}")
                .ToList();
            if (CmbToolPreset.Items.Count > 0) CmbToolPreset.SelectedIndex = 0;
        }
        catch
        {
            // A missing/corrupt database must not take down the whole panel.
        }
        finally { _loadingPresets = false; }

        SyncMaterialFromJob();   // P-302: a job created in Setup picks its material here
        ApplyPresetToFields();
    }

    /// <summary>
    /// P-302: select the current job's material (set in Setup) in the preset combo so
    /// the Cut stage resolves feeds for the material the operator actually chose.
    /// Setup names map onto the material database catalog: Pine→Softwood,
    /// Oak→Hardwood, Aluminum 6061→Aluminum, Plywood→Softwood. Unknown materials
    /// fall back to index 0. Public seam: tests drive this exact path.
    /// </summary>
    public void SyncMaterialFromJob()
    {
        var jobMaterial = AppState.CurrentJob?.ActiveSheet.Material?.Name;
        if (string.IsNullOrWhiteSpace(jobMaterial)) return;

        string target = MapSetupMaterial(jobMaterial);

        var names = CmbMaterialPreset.ItemsSource as IEnumerable<string>;
        int idx = names?.ToList()
            .FindIndex(n => string.Equals(n, target, StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (idx >= 0) CmbMaterialPreset.SelectedIndex = idx;
    }

    /// <summary>Setup combo name → material-database catalog name.</summary>
    public static string MapSetupMaterial(string setupName) => setupName.ToLowerInvariant() switch
    {
        "pine" => "Softwood",
        "plywood" => "Softwood",
        "oak" => "Hardwood",
        "mdf" => "MDF",
        "acrylic" => "Acrylic",
        var s when s.Contains("aluminum") || s.Contains("aluminium") => "Aluminum",
        var s when s.Contains("steel") => "Steel",
        _ => setupName   // pass through — exact-name matches still work
    };

    /// <summary>P-302 test seam: rerun preset population after a new job replaces
    /// the current one (the ctor already ran it for the previous job).</summary>
    public void PopulatePresetsForTest() => PopulatePresets();

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPresets) return;   // programmatic fills during Populate don't re-apply
        ApplyPresetToFields();
    }

    /// <summary>
    /// Resolve feed/plunge/RPM/depth for the current (tool, material, machine) and
    /// write them into the fields that BtnAdd_Click consumes. The user can still
    /// override anything by typing before pressing Add.
    /// </summary>
    public void ApplyPresetToFields()
    {
        EnsureDatabases();

        string? toolLabel = CmbToolPreset.SelectedItem as string;
        var tool = _toolDb!.Tools
            .OrderBy(t => t.DiameterMm)
            .FirstOrDefault(t => $"{t.Name}  ⌀{t.DiameterMm:0.##}" == toolLabel);
        if (tool is null) return;

        string? materialName = CmbMaterialPreset.SelectedItem as string;
        var resolved = tool.ResolvedCutData(materialName, AppState.Profile?.Name);

        TxtFeed.Text = resolved.FeedRateMmPerMin.ToString("0", CultureInfo.InvariantCulture);
        TxtDepth.Text = Math.Min(resolved.MaxDepthOfCutMm, resolved.MaxDepthOfCutMm > 0 ? resolved.MaxDepthOfCutMm : 2)
            .ToString("0.###", CultureInfo.InvariantCulture);

        // Remember RPM on the panel so Add can pass it through ParamsJson.
        PresetSpindleRpm = resolved.SpindleRpm;
    }

    /// <summary>RPM from the last preset resolution (0 when no preset applied).</summary>
    public double PresetSpindleRpm { get; private set; }

    // H-501 test seams (no InternalsVisibleTo): read the fields Add consumes and
    // drive the exact BtnAdd path minus its MessageBox guard.
    public string FeedFieldText => TxtFeed.Text;
    public string DepthFieldText => TxtDepth.Text;

    public void AddToolpathForTest(string name)
    {
        var layer = AppState.CurrentJob.ActiveSheet.ActiveLayer;
        if (layer is null || layer.Shapes.Count == 0) return;
        BtnAdd_Click(this, new RoutedEventArgs());
        if (AppState.Toolpaths.Toolpaths.Count > 0)
            AppState.Toolpaths.Toolpaths[^1].Name = name;
    }

    /// <summary>H-102: rebuild the Cuts list. Public so tests can prove a refresh keeps
    /// the selection and still yields real Toolpath items.</summary>
    public void RefreshCutsList() => RefreshList();

    /// <summary>H-211: the Toolpath OBJECTS currently shown as Cuts rows (tests).</summary>
    public System.Collections.Generic.IReadOnlyList<Toolpath> ToolpathListViewItems()
        => ToolpathList.Items.OfType<Toolpath>().ToList();

    private void RefreshList()
    {
        // H-102: add the Toolpath OBJECTS, not formatted strings. Six call-sites
        // (ArrayCopy_Click, the array/merge result selection, SaveTemplate_Click,
        // ApplyTemplate_Click) already do `SelectedItem is not Toolpath` — with strings
        // in the list those guards always failed and the buttons only ever refused.
        var keepId = (ToolpathList.SelectedItem as Toolpath)?.Id ?? _selectedToolpath?.Id;

        ToolpathList.Items.Clear();
        foreach (var tp in AppState.Toolpaths.Toolpaths)
            ToolpathList.Items.Add(tp);

        // A refresh used to drop the selection, blanking the params form mid-edit.
        if (keepId is { } id)
        {
            foreach (var item in ToolpathList.Items)
                if (item is Toolpath tp && tp.Id == id)
                {
                    ToolpathList.SelectedItem = tp;
                    break;
                }
        }
    }

    /// <summary>Strategy column: the exact registry key when we have one, else the enum.</summary>
    public static string StrategyLabel(Toolpath tp) =>
        string.IsNullOrWhiteSpace(tp.StrategyKey) ? tp.Strategy.ToString() : tp.StrategyKey!;

    /// <summary>Time column: m:ss, or an em dash when nothing has been estimated yet.</summary>
    public static string TimeLabel(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
        var total = (int)Math.Round(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60}:{total % 60:00}");
    }

    /// <summary>Dirty column: a warning marker for a toolpath whose G-code is stale.</summary>
    public static string DirtyLabel(bool isDirty) => isDirty ? "⚠" : "";

    private Toolpath? _selectedToolpath;

    private void ToolpathList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Prefer the bound object; fall back to the old index lookup so nothing regresses.
        if (ToolpathList.SelectedItem is Toolpath selected) _selectedToolpath = selected;
        else
        {
            var index = ToolpathList.SelectedIndex;
            _selectedToolpath = index >= 0 && index < AppState.Toolpaths.Toolpaths.Count
                ? AppState.Toolpaths.Toolpaths[index]
                : null;
        }
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

    /// <summary>
    /// Refill the strategy combo for the current <see cref="AppState.UiMode"/>. Called by the
    /// MainWindow mode toggle and by the job starters, so switching mode takes effect on an
    /// already-open panel instead of only at construction.
    /// </summary>
    public void RefreshForMode() => PopulateStrategies();

    /// <summary>
    /// Select a strategy by registry key. If the key is not visible in the current mode
    /// (e.g. a starter picks photo-vcarve, which is not on the Beginner list), switch to
    /// Advanced rather than silently leaving the user on the wrong operation.
    /// Returns true when the combo ended up on that key.
    /// </summary>
    public bool SelectStrategy(string strategyKey)
    {
        if (!UiModeCatalog.IsVisible(AppState.UiMode, strategyKey))
        {
            AppState.UiMode = UiMode.Advanced;
        }

        PopulateStrategies();

        for (int i = 0; i < CmbStrategy.Items.Count; i++)
        {
            if (CmbStrategy.Items[i] is StrategyRegistry.Entry entry && entry.Key == strategyKey)
            {
                CmbStrategy.SelectedIndex = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Fill the combo from the registry so every strategy is selectable.</summary>
    private void PopulateStrategies()
    {
        // Mode-filtered, so a beginner never meets "Thread Mill" or "Wrapped Fluting".
        // Advanced returns the whole registry unchanged.
        CmbStrategy.ItemsSource = UiModeCatalog.Filter(AppState.UiMode, Registry.Entries, e => e.Key);
        if (CmbStrategy.Items.Count > 0) CmbStrategy.SelectedIndex = 0;
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

            // H-501: the preset resolution's RPM rides along in ParamsJson so
            // Calculate emits M3 S<rpm> without retyping it per strategy.
            if (PresetSpindleRpm > 0)
            {
                try
                {
                    if (System.Text.Json.Nodes.JsonNode.Parse(tp.ParamsJson)?.AsObject() is { } obj &&
                        obj.ContainsKey("spindleRpm"))
                    {
                        obj["spindleRpm"] = PresetSpindleRpm;
                        tp.ParamsJson = obj.ToJsonString();
                    }
                }
                catch { /* params stay at defaults when JSON shape differs */ }
            }
        }
        else tp.ParamsJson = "{}";
        foreach (var s in layer.Shapes) tp.SelectedShapeIds.Add(s.Id);
        RefreshList();
        // Select the new toolpath so its params form is immediately editable.
        // Without this the user adds a strategy and the Params row stays blank.
        ToolpathList.SelectedIndex = AppState.Toolpaths.Toolpaths.Count - 1;
    }

    private async void BtnCalc_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.Toolpaths.Toolpaths.Count == 0)
        {
            MessageBox.Show("Add a toolpath first.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetCalcNote("");

        // A 4M-cell heightfield finish takes seconds of pure CPU. Running it inline froze
        // the whole window — no repaint, no E-stop, "not responding". Compute off the UI
        // thread and report progress instead.
        var toolpaths = AppState.Toolpaths.Toolpaths.ToList();

        BtnCalc.IsEnabled = false;
        try
        {
            for (int i = 0; i < toolpaths.Count; i++)
            {
                var tp = toolpaths[i];
                SetCalcNote($"Calculating {i + 1}/{toolpaths.Count}: {tp.Name}…");

                // Params form and status text are UI-thread only, so commit and validate
                // HERE, then hand only the pure computation to the worker.
                var prepared = PrepareForCompute(tp);
                if (prepared is null) continue;

                var (entry, shapes, heightfield) = prepared.Value;

                // Yield so the note actually paints before the CPU work starts.
                // Dispatcher.Yield is STATIC — an instance reference does not compile.
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.Background);

                var result = await Task.Run(() => entry.Compute(shapes, heightfield, tp.ParamsJson));

                ApplyComputeResult(tp, entry, result);
            }
            SetCalcNote("");
        }
        finally
        {
            BtnCalc.IsEnabled = true;
        }

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

    private void Merge_Click(object sender, RoutedEventArgs e) => DoMerge();

    /// <summary>
    /// Combine every calculated toolpath into one program. MergedToolpathEngine had no app
    /// call-site, so a multi-tool job had to be exported and streamed one file at a time.
    /// </summary>
    internal Toolpath? DoMerge()
    {
        var sources = AppState.Toolpaths.Toolpaths
            .Where(t => t.GCode.Count > 0 && t.GCode.Any(l =>
            {
                var s = l.TrimStart();
                return s.StartsWith("G0") || s.StartsWith("G1") || s.StartsWith("G2") || s.StartsWith("G3");
            }))
            .ToList();

        if (sources.Count == 0)
        {
            SetCalcNote("Nothing to merge — calculate at least one toolpath first.");
            return null;
        }
        if (sources.Count == 1)
        {
            SetCalcNote("Only one calculated toolpath — merging needs two or more.");
            return null;
        }

        // Toolpath carries ToolId (a Guid), not a tool NUMBER, while MergeSourceGcode wants
        // an int — so assign stable 1-based numbers per distinct tool. Sorting by that
        // number groups tool changes instead of interleaving them, which is the whole point
        // of a merged program. (MergeOrderStrategy has no ToolNumber member; the ordering is
        // done here.)
        var toolNumbers = sources
            .Select(t => t.ToolId)
            .Distinct()
            .Select((id, i) => (id, number: i + 1))
            .ToDictionary(x => x.id, x => x.number);

        var ordered = sources
            .OrderBy(t => toolNumbers[t.ToolId])
            .ToList();

        var result = MergedToolpathEngine.Compute(
            ordered.Select(t => new MergeSourceGcode
            {
                Name = t.Name,
                Id = t.Id,
                ToolNumber = toolNumbers[t.ToolId],
                GcodeLines = t.GCode
            }).ToList(),
            MergeOrderStrategy.SelectionOrder,   // already tool-ordered above
            MergeMode.Union,
            keepOriginals: true);

        if (!result.Success || result.GcodeLines.Count == 0)
        {
            SetCalcNote(result.ErrorMessage ?? "The merge produced no moves.");
            return null;
        }

        // A NEW toolpath, so the originals survive and "undo" is deleting this one.
        var merged = AppState.Toolpaths.Add(sources[0].Strategy);
        merged.Name = $"Merged ({sources.Count} toolpaths)";
        merged.StrategyKey = sources[0].StrategyKey;
        merged.GCode.Clear();
        merged.GCode.AddRange(result.GcodeLines);
        merged.IsDirty = false;

        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        RefreshList();
        ToolpathList.SelectedItem = merged;
        SetCalcNote($"Merged {sources.Count} toolpaths — {result.TotalSegments} segments, originals kept.");
        return merged;
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
    /// Delegates to JobGate so Machine's Start refuses with the SAME message: the two used
    /// to disagree, and Start had no validation at all.
    /// </summary>
    public static string? AreaStrategyBlocker(string strategyKey, string displayName, IReadOnlyList<VectorShape> shapes)
        => JobGate.AreaStrategyBlocker(strategyKey, displayName, shapes);

    /// <summary>
    /// Merge the Tabs box into the toolpath's ParamsJson, so holding tabs reach the SAME
    /// program the Machine stage streams.
    ///
    /// ProfileParams.TabCount and the engine's tab logic both already worked; the gap was
    /// the UI. tabCount defaulted to 0 with no control, so a user could not ask for tabs at
    /// all and every profiled part came loose on the last pass.
    /// </summary>
    internal void ApplyTabCount(Toolpath tp)
    {
        if (TxtTabCount is null) return;
        if (!StrategyKeyMap.IsProfileLike(tp.StrategyKey)) return;
        if (!int.TryParse(TxtTabCount.Text, out int tabs) || tabs < 0) return;

        tp.ParamsJson = MergeParam(tp.ParamsJson, "tabCount", tabs);
    }

    /// <summary>Set one numeric key in a params JSON object, preserving the rest.</summary>
    public static string MergeParam(string paramsJson, string key, double value)
    {
        Dictionary<string, System.Text.Json.JsonElement> map;
        try
        {
            map = System.Text.Json.JsonSerializer
                      .Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(paramsJson)
                  ?? new Dictionary<string, System.Text.Json.JsonElement>();
        }
        catch (System.Text.Json.JsonException)
        {
            map = new Dictionary<string, System.Text.Json.JsonElement>();
        }

        using var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(value));
        map[key] = doc.RootElement.Clone();

        return System.Text.Json.JsonSerializer.Serialize(map);
    }

    /// <summary>
    /// UI-thread half of a recalculation: commit the params form, resolve the strategy and
    /// selection, and reject the cases that need a message. Returns null when nothing
    /// should be computed (the reason is already on screen).
    /// </summary>
    private (StrategyRegistry.Entry Entry, List<VectorShape> Shapes, HeightfieldData? Heightfield)? PrepareForCompute(Toolpath tp)
    {
        // Commit the params form (expression resolution) before dispatch.
        CommitParamsForm(tp);
        ApplyTabCount(tp);
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) return null;
        var shapes = layer.Shapes.Where(s => tp.SelectedShapeIds.Contains(s.Id)).ToList();
        if (shapes.Count == 0)
        {
            SetCalcNote($"{tp.Name}: no source shapes — select geometry in Design.");
            return null;
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
            return null;
        }

        if (entry.UsesHeightfield && AppState.Heightfield is null)
        {
            tp.GCode.Clear();
            tp.GCode.Add($"({entry.DisplayName}: needs a 3D relief — bake one in the Model stage)");
            tp.IsDirty = true;
            SetCalcNote($"{entry.DisplayName} needs a relief. Build one in the Model stage, then Calculate.");
            return null;
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
            return null;
        }

        return (entry, shapes, AppState.Heightfield);
    }

    /// <summary>UI-thread half: store the computed program, or explain why there is none.</summary>
    private void ApplyComputeResult(Toolpath tp, StrategyRegistry.Entry entry, StrategyResult result)
    {
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

    /// <summary>
    /// Synchronous recalculation, for the single-toolpath paths (selection change, Recalc
    /// Dirty). BtnCalc_Click uses the async split so a 4M-cell relief cannot freeze the UI.
    /// </summary>
    private void RecalculateToolpath(Toolpath tp)
    {
        if (PrepareForCompute(tp) is not { } prepared) return;
        var (entry, shapes, heightfield) = prepared;
        ApplyComputeResult(tp, entry, entry.Compute(shapes, heightfield, tp.ParamsJson));
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

/// <summary>H-102: Strategy column — registry key when set, else the coarser enum.</summary>
public sealed class ToolpathStrategyConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Toolpath tp ? CutPanel.StrategyLabel(tp) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>H-102: Time column — EstimatedTimeSeconds as m:ss, or a placeholder at zero.</summary>
public sealed class ToolpathTimeConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Toolpath tp ? CutPanel.TimeLabel(tp.EstimatedTimeSeconds) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>H-102: Dirty column — a marker only when the G-code is stale.</summary>
public sealed class ToolpathDirtyConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Toolpath tp ? CutPanel.DirtyLabel(tp.IsDirty) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
