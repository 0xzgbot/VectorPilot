using System.Windows;
using System.Windows.Controls;
using VectorPilot.App.Controls;

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
}
