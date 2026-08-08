using System.IO;
using System.Windows;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class PostManagerDialog : Window
{
    private readonly PostCatalog _catalog;

    public PostManagerDialog()
    {
        InitializeComponent();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VectorPilot", "posts.json");
        _catalog = new PostCatalog(path).WithDefaults();
        Refresh();
    }

    private void Refresh()
    {
        PostList.ItemsSource = null;
        PostList.ItemsSource = _catalog.Posts
            .OrderBy(p => p.Name)
            .Select(p => $"{p.Name}  v{p.Version}{(p.IsLatest ? "  ★ Latest" : "")}  → {p.Extension}")
            .ToList();
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        var version = TxtVersion.Text.Trim();
        if (name.Length == 0 || version.Length == 0) return;
        _catalog.Install(new PostDefinition { Name = name, Version = version, IsLatest = false });
        Refresh();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var line = PostList.SelectedItem?.ToString();
        if (line is null) return;
        var parts = line.Split("  v");
        if (parts.Length < 2) return;
        var version = parts[1].Split(' ')[0];
        _catalog.Remove(parts[0].Trim(), version);
        Refresh();
    }
}
