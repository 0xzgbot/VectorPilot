using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>Card A4: tool database browser — class tree + cut-data form.</summary>
public partial class ToolBrowserDialog : Window
{
    private readonly ToolBrowserViewModel _vm;
    private readonly string _dbPath;
    private Tool? _current;

    public ToolBrowserDialog(ToolDatabase db, string dbPath)
    {
        InitializeComponent();
        _vm = new ToolBrowserViewModel(db);
        _dbPath = dbPath;

        MaterialPicker.ItemsSource = new[] { "hardwood", "softwood", "plastic", "aluminum", "steel" };
        MaterialPicker.SelectedIndex = 0;

        var machines = new List<string> { "(none — use material data)" };
        machines.AddRange(db.Tools.SelectMany(t => t.MachineCutData).Select(m => m.MachineName).Distinct());
        MachinePicker.ItemsSource = machines;
        MachinePicker.SelectedIndex = 0;

        BuildTree();
    }

    private void BuildTree()
    {
        ToolTree.Items.Clear();
        foreach (var cls in _vm.Classes)
        {
            var node = new TreeViewItem { Header = $"{cls.DisplayName()}", IsExpanded = true };
            foreach (var tool in _vm.ToolsOfClass(cls))
            {
                node.Items.Add(new TreeViewItem { Header = tool.Name, Tag = tool });
            }
            ToolTree.Items.Add(node);
        }
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void ToolTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _current = (e.NewValue as TreeViewItem)?.Tag as Tool;
        if (_current is null) { ToolTitle.Text = "Select a tool"; ToolGeom.Text = ""; return; }

        ToolTitle.Text = _current.Name;
        ToolGeom.Text = $"{_current.Type.DisplayName()} · Ø{F(_current.DiameterMm)} mm · {_current.Flutes} flute(s) · " +
                        $"cut {F(_current.CuttingLengthMm)} mm · shank Ø{F(_current.ShankDiameterMm)} mm";
        LoadForm();
    }

    private void LoadForm()
    {
        if (_current is null) return;
        var staged = _vm.PendingFor(_current);
        if (staged is not null)
        {
            FeedBox.Text = F(staged.FeedRateMmPerMin);
            PlungeBox.Text = F(staged.PlungeRateMmPerMin);
            RpmBox.Text = F(staged.SpindleRpm);
            DepthBox.Text = F(staged.MaxDepthOfCutMm);
            StatusNote.Text = "staged (unsaved)";
            return;
        }

        var r = _vm.Resolve(_current);
        FeedBox.Text = F(r.FeedRateMmPerMin);
        PlungeBox.Text = F(r.PlungeRateMmPerMin);
        RpmBox.Text = F(r.SpindleRpm);
        DepthBox.Text = F(r.MaxDepthOfCutMm);
        StatusNote.Text = "";
    }

    private void Context_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _vm.Material = MaterialPicker.SelectedItem as string;
        _vm.MachineName = MachinePicker.SelectedIndex <= 0 ? null : MachinePicker.SelectedItem as string;
        ResolveNote.Text = _vm.MachineName is null
            ? "resolving: material → derived"
            : $"resolving: machine '{_vm.MachineName}' → material → derived";
        LoadForm();
    }

    private void Stage_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (!double.TryParse(FeedBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double feed) ||
            !double.TryParse(PlungeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double plunge) ||
            !double.TryParse(RpmBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rpm) ||
            !double.TryParse(DepthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double depth))
        {
            StatusNote.Text = "All four fields must be numbers.";
            return;
        }
        _vm.Edit(_current, feed, plunge, rpm, depth);
        StatusNote.Text = "staged (unsaved)";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int n = _vm.Save();
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            _vm.Database.SaveToJson(_dbPath);
            StatusNote.Text = $"saved {n} tool(s)";
        }
        catch (Exception ex)
        {
            StatusNote.Text = $"save failed: {ex.Message}";
        }
        LoadForm();
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        _vm.Revert();
        StatusNote.Text = "reverted";
        LoadForm();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
