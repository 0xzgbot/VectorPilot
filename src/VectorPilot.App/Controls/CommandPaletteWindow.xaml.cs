using System.Windows;
using System.Windows.Input;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class CommandPaletteWindow : Window
{
    private readonly CommandRegistry _registry;

    public CommandPaletteWindow(CommandRegistry registry)
    {
        InitializeComponent();
        _registry = registry;
        Refresh("", "");
        SearchBox.Focus();
    }

    private void Refresh(string query, string _)
    {
        ResultsBox.ItemsSource = null;
        ResultsBox.ItemsSource = _registry.Search(query).Select(c => c.Shortcut is null ? $"{c.Title}" : $"{c.Title}   [{c.Shortcut}]").ToList();
        ResultsBox.SelectedIndex = ResultsBox.Items.Count > 0 ? 0 : -1;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => Refresh(SearchBox.Text, "");

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ResultsBox.Items.Count > 0)
        {
            ResultsBox.SelectedIndex = (ResultsBox.SelectedIndex + 1) % ResultsBox.Items.Count;
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ResultsBox.Items.Count > 0)
        {
            ResultsBox.SelectedIndex = (ResultsBox.SelectedIndex - 1 + ResultsBox.Items.Count) % ResultsBox.Items.Count;
            e.Handled = true;
        }
    }

    private void ResultsBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExecuteSelected();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void ExecuteSelected()
    {
        if (ResultsBox.SelectedIndex < 0 || ResultsBox.SelectedIndex >= _registry.Search(SearchBox.Text).Count()) return;
        var cmd = _registry.Search(SearchBox.Text).ElementAt(ResultsBox.SelectedIndex);
        Close();
        cmd.Execute();
    }
}
