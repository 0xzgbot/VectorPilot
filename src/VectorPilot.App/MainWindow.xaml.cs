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

    public MainWindow()
    {
        InitializeComponent();
        _machine.RailStatusChanged += s => RailStatus.Text = s;
        _machine.DocumentTitleChanged += t => DocTitle.Text = t;
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
        Dispatcher.BeginInvoke(new Action(PromptForRecovery), DispatcherPriority.Loaded);
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
        StageHost.Content = tag switch
        {
            "design" => _design,
            "model" => _model,
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
