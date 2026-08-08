using System.IO;
using System.Windows;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class MachineConfigDialog : Window
{
    private readonly MachineConfigDatabase _db;

    public MachineConfigDialog()
    {
        InitializeComponent();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VectorPilot", "machines.json");
        _db = new MachineConfigDatabase(path).WithDefaults();
        Refresh();
    }

    private void Refresh()
    {
        MachineList.ItemsSource = null;
        MachineList.ItemsSource = _db.Machines.Select(m => $"{m.Name}  ({m.TravelXmm:0}×{m.TravelYmm:0}×{m.TravelZmm:0}mm, {m.Axes} axis)");
    }

    private void MachineList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var name = MachineList.SelectedItem?.ToString()?.Split("(")[0].Trim();
        if (name is null || _db.Find(name) is not { } m) return;
        TxtName.Text = m.Name;
        TxtX.Text = m.TravelXmm.ToString();
        TxtY.Text = m.TravelYmm.ToString();
        TxtZ.Text = m.TravelZmm.ToString();
        TxtAxes.Text = m.Axes.ToString();
        TxtPort.Text = m.Port;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        var name = MachineList.SelectedItem?.ToString()?.Split("(")[0].Trim();
        if (name is null || _db.Find(name) is not { } m) return;
        AppState.Profile = m.ToProfile();
        MessageBox.Show($"Machine set to {m.Name}.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var name = MachineList.SelectedItem?.ToString()?.Split("(")[0].Trim();
        if (name is null) return;
        _db.Delete(name);
        Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (name.Length == 0) return;
        var m = _db.Find(name) ?? new MachineConfigEntry();
        m.Name = name;
        m.TravelXmm = ParseDouble(TxtX.Text) ?? m.TravelXmm;
        m.TravelYmm = ParseDouble(TxtY.Text) ?? m.TravelYmm;
        m.TravelZmm = ParseDouble(TxtZ.Text) ?? m.TravelZmm;
        m.Axes = (int)(ParseDouble(TxtAxes.Text) ?? m.Axes);
        m.Port = TxtPort.Text.Trim();
        if (_db.Find(name) is null) _db.Add(m);
        else _db.Save();
        Refresh();
    }

    private static double? ParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}
