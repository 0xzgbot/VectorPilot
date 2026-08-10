using System.IO;
using System.Windows;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class PreferencesWindow : Window
{
    private readonly PreferencesStore _store;

    public PreferencesWindow()
    {
        InitializeComponent();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VectorPilot", "preferences.json");
        _store = new PreferencesStore(path);

        CmbUnits.SelectedIndex = _store.Value.Units == "inch" ? 1 : 0;
        CmbTheme.SelectedIndex = _store.Value.Theme == "Light" ? 1 : 0;
        TxtAutosave.Text = _store.Value.AutosaveIntervalSeconds.ToString();
        ChkGrid.IsChecked = _store.Value.ShowGrid;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(p =>
        {
            p.Units = (CmbUnits.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "mm";
            p.Theme = (CmbTheme.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Dark";
            p.AutosaveIntervalSeconds = int.TryParse(TxtAutosave.Text, out var s) ? Math.Max(30, s) : 300;
            p.ShowGrid = ChkGrid.IsChecked ?? true;
        });
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
