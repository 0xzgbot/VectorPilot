using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VectorPilot.App.Controls;

/// <summary>
/// Keyboard shortcut remapping (Mac SPK-UXPOLISH parity). Captures a real key
/// gesture from the user, rejects conflicts, and persists overrides to LocalAppData.
/// </summary>
public partial class ShortcutDialog : Window
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VectorPilot", "shortcuts.json");

    private readonly ShortcutStore _store;
    private string? _selectedId;

    public ShortcutDialog() : this(Load()) { }

    public ShortcutDialog(ShortcutStore store)
    {
        InitializeComponent();
        _store = store;
        Loaded += (_, _) => RefreshList();
    }

    /// <summary>Load persisted overrides, tolerating a missing or corrupt file.</summary>
    public static ShortcutStore Load()
    {
        var store = new ShortcutStore();
        try
        {
            if (File.Exists(StorePath)) store.LoadJson(File.ReadAllText(StorePath));
        }
        catch (IOException) { /* defaults */ }
        return store;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, _store.ToJson());
        }
        catch (IOException ex)
        {
            Note.Text = $"Could not save: {ex.Message}";
        }
    }

    private void RefreshList()
    {
        int keep = CommandList.SelectedIndex;
        CommandList.ItemsSource = _store.CommandIds
            .OrderBy(id => id)
            .Select(id => new Row(id, _store.Gesture(id) ?? "", _store.IsRemapped(id)))
            .ToList();
        CommandList.DisplayMemberPath = nameof(Row.Display);
        CommandList.SelectedIndex = keep;
    }

    private sealed record Row(string Id, string Gesture, bool Remapped)
    {
        public string Display => $"{Id,-14} {Gesture}{(Remapped ? "   (custom)" : "")}";
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedId = (CommandList.SelectedItem as Row)?.Id;
        CaptureLabel.Text = _selectedId is null
            ? "No command selected"
            : $"{_selectedId} — currently {_store.Gesture(_selectedId)}";
        CaptureBox.Text = "press a key combination…";
        Note.Text = "";
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_selectedId is null) { Note.Text = "Select a command first."; return; }

        // Ignore bare modifier presses — wait for the actual key.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                  or Key.LeftAlt or Key.RightAlt or Key.System) return;

        string gesture = Describe(e.Key, Keyboard.Modifiers);
        CaptureBox.Text = gesture;

        var holder = _store.CommandFor(gesture);
        if (holder is not null && holder != _selectedId)
        {
            Note.Text = $"{gesture} is already bound to '{holder}'.";
            return;
        }

        if (_store.Remap(_selectedId, gesture))
        {
            Note.Text = "";
            Save();
            RefreshList();
        }
        else
        {
            Note.Text = "That combination cannot be assigned.";
        }
    }

    private static string Describe(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private void ResetOne_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null) return;
        _store.ResetCommand(_selectedId);
        Save();
        RefreshList();
        CaptureBox.Text = _store.Gesture(_selectedId) ?? "";
        Note.Text = "";
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _store.ResetAll();
        Save();
        RefreshList();
        Note.Text = "All shortcuts restored to defaults.";
    }
}
