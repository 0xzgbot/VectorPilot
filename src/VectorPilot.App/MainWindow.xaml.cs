using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VectorPilot.App.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App;

public partial class MainWindow : Window
{
    private readonly SetupPanel _setup = new();
    private readonly DesignPanel _design = new();
    private readonly ModelPanel _model = new();
    private readonly CutPanel _cut = new();
    private readonly MachinePanel _machine = new();
    private readonly OutputPanel _output = new();
    private readonly PhotoPanel _photo = new();

    public MainWindow()
    {
        InitializeComponent();
        _machine.RailStatusChanged += s => RailStatus.Text = s;
        _machine.DocumentTitleChanged += t => DocTitle.Text = t;

        // H-103: the machine is always on screen. The dock owns the app-lifetime session;
        // the Machine panel hands its session over on connect and adopts the dock's.
        MachineDock.MachineStageRequested += () => Stage_ClickByTag("machine");
        MachineDock.DockMessage += s => RailStatus.Text = s;
        _machine.SessionCreated += s =>
        {
            if (s is not null) MachineDock.Adopt(s, s.Transport);
        };
        _design.AttachMachineDock(MachineDock);   // H-104: Frame + click-to-jog on Design

        StageHost.Content = _setup;
        PreviewKeyDown += MainWindow_PreviewKeyDown;   // Ctrl+K palette hook
        StartAutosaveTimer();                           // crash-recovery autosave
        CheckForRecoverableWork();                      // .autosave newer than save?
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var w = new Controls.CommandPaletteWindow(PaletteCommands) { Owner = this };
            w.ShowDialog();
            e.Handled = true;
        }
    }

    // ---- H-101: Beginner/Advanced mode + job starters ----

    /// <summary>Guard so mirroring the combo does not re-enter UiMode_Changed.</summary>
    private bool _syncingModeCombo;

    private void UiMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbUiMode is null || _syncingModeCombo) return;

        AppState.UiMode = CmbUiMode.SelectedIndex == 1 ? UiMode.Advanced : UiMode.Beginner;

        // Refresh an already-built panel; without this the mode only took effect on a panel
        // constructed after the switch.
        _cut.RefreshForMode();
    }

    /// <summary>
    /// Make the rail combo show the mode that is actually in effect. Called after anything
    /// that can change the mode behind the UI's back (a job starter promoting to Advanced).
    /// </summary>
    private void SyncModeCombo()
    {
        if (CmbUiMode is null) return;

        int want = AppState.UiMode == UiMode.Advanced ? 1 : 0;
        if (CmbUiMode.SelectedIndex == want) return;

        // Assigning SelectedIndex raises SelectionChanged, which would write AppState.UiMode
        // back from the combo — harmless here but circular, so suppress it.
        _syncingModeCombo = true;
        try { CmbUiMode.SelectedIndex = want; }
        finally { _syncingModeCombo = false; }
    }

    private void JobStarter_Click(object sender, RoutedEventArgs e) => ShowJobStarter();

    /// <summary>
    /// Show the three job starters. Returns the overlay so tests can drive the same instance
    /// the button creates. Public because the test project has no InternalsVisibleTo.
    /// </summary>
    public Controls.JobStarterOverlay ShowJobStarter()
    {
        var overlay = new Controls.JobStarterOverlay();
        overlay.Started += (kind, strategyKey) =>
        {
            StarterHost.Content = null;
            StarterHost.Visibility = Visibility.Collapsed;

            // Select FIRST: SelectStrategy may promote to Advanced (photo-vcarve is not a
            // Beginner operation). Setting the rail combo before this left it reading
            // "Beginner" while AppState.UiMode was Advanced — the label and the state
            // disagreed, and the combo is what the user believes.
            if (strategyKey is not null)
            {
                _cut.SelectStrategy(strategyKey);
                StageHost.Content = _cut;      // land the user on the operation itself
            }

            // Now mirror whatever the mode actually ended up as.
            SyncModeCombo();
        };

        StarterHost.Content = overlay;
        StarterHost.Visibility = Visibility.Visible;
        return overlay;
    }

    private System.Windows.Threading.DispatcherTimer? _autosaveTimer;

    /// <summary>Per-user state root. Local (not Roaming): this is machine-specific.</summary>
    private static string StateDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VectorPilot");

    private static string AutosaveDir() => Path.Combine(StateDir(), "autosave.shoppilot");

    private void StartAutosaveTimer()
    {
        var prefs = new PreferencesStore(
            Path.Combine(StateDir(), "preferences.json"));
        _autosaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, prefs.Value.AutosaveIntervalSeconds))
        };
        _autosaveTimer.Tick += (_, _) => AutosaveNow();
        _autosaveTimer.Start();
    }

    private void AutosaveNow()
    {
        try
        {
            var job = AppState.CurrentJob;
            var toolpaths = AppState.Toolpaths.Toolpaths.Select(VectorPilot.Engine.ToolpathPersistence.ToPersisted).ToList();
            VectorPilot.Engine.IO.DocumentSaver.Save(job, toolpaths, AutosaveDir());
        }
        catch
        {
            // Autosave failures are silent — the manual save path surfaces errors.
        }
    }

    private void CheckForRecoverableWork()
    {
        // Deferred: a modal in the ctor blocks before the window renders, which
        // gates startup (and hung --ui-smoke). Ask once the shell is up, and skip
        // entirely in automation.
        if (App.IsAutomated) return;
        Dispatcher.BeginInvoke(new Action(ShowWelcomeThenRecovery), DispatcherPriority.Loaded);
    }

    private readonly FirstRunState _firstRun = new();

    /// <summary>
    /// First-run welcome, then the recovery prompt. Sequenced so two modals never
    /// fight over the same startup moment.
    /// </summary>
    private void ShowWelcomeThenRecovery()
    {
        if (_firstRun.IsFirstRun)
        {
            var dlg = new WelcomeDialog { Owner = this };
            dlg.ShowDialog();

            if (dlg.SuppressFuture || dlg.ChosenAction is not null) _firstRun.MarkShown();

            switch (dlg.ChosenAction)
            {
                case "recipe":
                    PaletteCommands.Search("recipe").FirstOrDefault()?.Execute();
                    return;                       // the recipe replaces the job
                case "blank":
                    break;                        // the default empty job is already loaded
            }
        }

        PromptForRecovery();
    }

    private void PromptForRecovery()
    {
        try
        {
            var autosaveDir = AutosaveDir();
            if (!VectorPilot.Engine.IO.DocumentSaver.Exists(autosaveDir)) return;
            if (AppState.CurrentJob.FilePath is { } manualPath && File.Exists(manualPath) &&
                File.GetLastWriteTimeUtc(manualPath) >= Directory.GetLastWriteTimeUtc(autosaveDir))
            {
                return; // manual save is newer — nothing to recover
            }
            var result = MessageBox.Show(
                "VectorPilot found an autosave newer than the last manual save.\n\nRecover it?",
                "Recover unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            var loaded = VectorPilot.Engine.IO.DocumentLoader.Load(autosaveDir);
            if (loaded.Job is { } job)
            {
                AppState.RestoreJob(job);
                if (loaded.Toolpaths is { } toolpaths)
                {
                    AppState.Toolpaths.Toolpaths.Clear();
                    AppState.Toolpaths.Toolpaths.AddRange(toolpaths.Select(VectorPilot.Engine.ToolpathPersistence.FromPersisted));
                }
            }
        }
        catch
        {
            // Recovery prompt must never crash startup.
        }
    }

    /// <summary>Card P1: refresh every stage after a recipe replaces the job.</summary>
    internal void ReloadAfterRecipe()
    {
        _design.RefreshIfVisible();
        _output.Refresh();
        StageHost.Content = _design;
        DocTitle.Text = AppState.CurrentJob.Name;
    }

    private void Stage_Click(object sender, RoutedEventArgs e)
    {
        var tag = ((Button)sender).Tag as string;
        Stage_ClickByTag(tag);
    }

    /// <summary>Stage switch by tag, so the dock's Connect… can route to the Machine stage.</summary>
    private void Stage_ClickByTag(string? tag)
    {
        StageHost.Content = tag switch
        {
            "design" => _design,
            "model" => _model,
            "photo" => _photo,
            "cut" => _cut,
            "machine" => _machine,
            "output" => _output,
            _ => _setup
        };
        if (tag is "design" or "cut" or "output") _design.RefreshIfVisible();
    }

    private static readonly CommandRegistry PaletteCommands = BuildPaletteCommands();

    private static CommandRegistry BuildPaletteCommands()
    {
        var reg = new CommandRegistry();
        reg.Register(new CommandRegistry.Command("materials", "Material Settings…", null, "Tools", () =>
        {
            var dlg = new Controls.MaterialDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }));
        reg.Register(new CommandRegistry.Command("machines", "Machine Configuration…", null, "Tools", () =>
        {
            var dlg = new Controls.MachineConfigDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }));
        reg.Register(new CommandRegistry.Command("posts", "Post Processors…", null, "Tools", () =>
        {
            var dlg = new Controls.PostManagerDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }));
        reg.Register(new CommandRegistry.Command("gadget", "Run Lua Gadget…", null, "Tools", () =>
        {
            var dlg = new Controls.GadgetDialog { Owner = Application.Current?.MainWindow };
            dlg.ShowDialog();
        }));

        reg.Register(new CommandRegistry.Command("shortcuts", "Keyboard Shortcuts…", null, "Tools", () =>
        {
            var dlg = new Controls.ShortcutDialog { Owner = Application.Current?.MainWindow };
            dlg.ShowDialog();
        }));

        reg.Register(new CommandRegistry.Command("recipe", "New from Recipe…", null, "File", () =>
        {
            var owner = Application.Current?.MainWindow;
            var dlg = new RecipeDialog { Owner = owner };
            if (dlg.ShowDialog() == true && dlg.CreatedJob is { } job)
            {
                AppState.RestoreJob(job);
                if (owner is MainWindow mw) mw.ReloadAfterRecipe();
            }
        }));

        reg.Register(new CommandRegistry.Command("tools", "Tool Database…", null, "Tools", () =>
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VectorPilot", "tools.json");
            var db = System.IO.File.Exists(path) ? ToolDatabase.LoadFromJson(path) : new ToolDatabase(seedDefaults: true);
            var dlg = new Controls.ToolBrowserDialog(db, path) { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        }));

        reg.Register(new CommandRegistry.Command("palette", "Command Palette…", "Ctrl+K", "Tools", () =>
        {
            var w = new Controls.CommandPaletteWindow(reg) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
        }));
        reg.Register(new CommandRegistry.Command("preferences", "Preferences…", null, "Tools", () =>
        {
            var w = new Controls.PreferencesWindow { Owner = Application.Current.MainWindow };
            w.ShowDialog();
        }));
        return reg;
    }

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var cmd in PaletteCommands.Commands)
        {
            var item = new System.Windows.Controls.MenuItem { Header = cmd.Title };
            item.Click += (_, _) => cmd.Execute();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
