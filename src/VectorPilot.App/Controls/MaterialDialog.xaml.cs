using System.IO;
using System.Windows;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class MaterialDialog : Window
{
    private readonly MaterialDatabase _db;

    public MaterialDialog()
    {
        InitializeComponent();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VectorPilot", "materials.json");
        _db = new MaterialDatabase(path).WithDefaults();
        Refresh();
    }

    private void Refresh()
    {
        MaterialList.ItemsSource = null;
        MaterialList.ItemsSource = _db.Materials.Select(m => $"{m.Name}  —  {m.RecommendedFeedRate ?? 0:0} mm/min");
    }

    private void MaterialList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var name = MaterialList.SelectedItem?.ToString()?.Split("—")[0].Trim();
        if (name is null || _db.Find(name) is not { } m) return;
        TxtName.Text = m.Name;
        TxtFeed.Text = m.RecommendedFeedRate?.ToString() ?? "";
        TxtPlunge.Text = m.RecommendedPlungeRate?.ToString() ?? "";
        TxtSpindle.Text = m.RecommendedSpindleSpeed?.ToString() ?? "";
        TxtDepth.Text = m.MaxDepthOfCutMm.ToString();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _db.Add(new Material { Name = "New Material" });
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var name = MaterialList.SelectedItem?.ToString()?.Split("—")[0].Trim();
        if (name is null) return;
        _db.Delete(name);
        Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (name.Length == 0) return;
        var m = _db.Find(name) ?? new Material();
        m.Name = name;
        m.RecommendedFeedRate = ParseDouble(TxtFeed.Text);
        m.RecommendedPlungeRate = ParseDouble(TxtPlunge.Text);
        m.RecommendedSpindleSpeed = ParseDouble(TxtSpindle.Text);
        m.MaxDepthOfCutMm = ParseDouble(TxtDepth.Text) ?? 6;
        if (_db.Find(name) is null) _db.Add(m);
        else _db.Save();
        Refresh();
    }

    private static double? ParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}
