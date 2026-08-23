using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>Card A6: component tree + combine modes + sculpt brush controls.</summary>
public partial class ComponentTreePanel : UserControl
{
    // H-211: the panel's stack. Defaults to its own; UseSharedStack swaps in the
    // app-wide VM so every stage edits one component stack.
    internal ComponentTreeViewModel Vm { get; private set; } = new();
    private bool _ready;

    public ComponentTreePanel()
    {
        InitializeComponent();

        ModePicker.ItemsSource = Enum.GetValues<OperationMode>();
        ToolPicker.ItemsSource = Enum.GetValues<SculptTool>();
        ShapePicker.ItemsSource = Enum.GetValues<BrushShape>();
        FalloffPicker.ItemsSource = Enum.GetValues<BrushFalloff>();

        ToolPicker.SelectedItem = SculptTool.Brush;
        ShapePicker.SelectedItem = Vm.BrushShape;
        FalloffPicker.SelectedItem = Vm.BrushFalloff;

        // Handlers attach after the visual tree exists (A5 lesson: XAML-attached
        // handlers fire during init before siblings are created).
        Loaded += (_, _) => { _ready = true; Refresh(); };
    }

    /// <summary>
    /// H-211: point this panel at the app-wide shared stack so components created on
    /// other stages (e.g. a grayscale photo relief) show up here. Brush settings are
    /// copied onto the shared VM so the sliders keep driving the active stack.
    /// </summary>
    internal void UseSharedStack(ComponentTreeViewModel shared)
    {
        var brushShape = Vm.BrushShape;
        var brushFalloff = Vm.BrushFalloff;
        double radius = Vm.BrushRadiusMm, strength = Vm.BrushStrength;
        Vm = shared;
        Vm.BrushShape = brushShape;
        Vm.BrushFalloff = brushFalloff;
        Vm.BrushRadiusMm = radius;
        Vm.BrushStrength = strength;
        if (_ready) Refresh();
    }

    internal void Refresh()
    {
        ComponentList.ItemsSource = null;
        ComponentList.ItemsSource = Vm.Components;
        if (Vm.SelectedIndex >= 0 && Vm.SelectedIndex < Vm.Components.Count)
            ComponentList.SelectedIndex = Vm.SelectedIndex;

        if (Vm.Selected is { } sel) ModePicker.SelectedItem = sel.CombineMode;

        StatusLabel.Text = Vm.Components.Count == 0
            ? "No components"
            : $"{Vm.Components.Count} component(s) · {Vm.Components.Count(c => c.Visible)} visible" +
              (Vm.Composite is null ? " · composite: none" : $" · composite {Vm.Composite.Width}×{Vm.Composite.Height}");
    }

    private void ComponentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Vm.SelectedIndex = ComponentList.SelectedIndex;
        if (Vm.Selected is { } sel) ModePicker.SelectedItem = sel.CombineMode;
    }

    private void Visible_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not CheckBox { Tag: ReliefComponent c } box) return;
        Vm.SetVisible(c, box.IsChecked == true);
        Refresh();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || Vm.Selected is null || ModePicker.SelectedItem is not OperationMode mode) return;
        Vm.SetMode(Vm.Selected, mode);
        Refresh();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.MoveTo(Vm.SelectedIndex, Vm.SelectedIndex - 1)) Refresh();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.MoveTo(Vm.SelectedIndex, Vm.SelectedIndex + 1)) Refresh();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is { } sel && Vm.Remove(sel)) Refresh();
    }

    private void Brush_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (ShapePicker.SelectedItem is BrushShape s) Vm.BrushShape = s;
        if (FalloffPicker.SelectedItem is BrushFalloff f) Vm.BrushFalloff = f;
        Vm.BrushRadiusMm = RadiusSlider.Value;
        Vm.BrushStrength = StrengthSlider.Value;
    }

    // Slider fires RoutedPropertyChangedEventArgs<double>; forward to the shared handler.
    private void Brush_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => Brush_Changed(sender, (RoutedEventArgs)e);
}
