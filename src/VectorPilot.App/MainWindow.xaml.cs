using System.Windows;
using System.Windows.Controls;
using VectorPilot.App.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App;

public partial class MainWindow : Window
{
    private readonly SetupPanel _setup = new();
    private readonly DesignPanel _design = new();
    private readonly CutPanel _cut = new();
    private readonly MachinePanel _machine = new();
    private readonly OutputPanel _output = new();

    public MainWindow()
    {
        InitializeComponent();
        _machine.RailStatusChanged += s => RailStatus.Text = s;
        _machine.DocumentTitleChanged += t => DocTitle.Text = t;
        StageHost.Content = _setup;
    }

    private void Stage_Click(object sender, RoutedEventArgs e)
    {
        var tag = ((Button)sender).Tag as string;
        StageHost.Content = tag switch
        {
            "design" => _design,
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
