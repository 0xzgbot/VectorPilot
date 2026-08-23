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
        FadeDirectionPicker.ItemsSource = Enum.GetValues<FadeDirection>();

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
        LoadModifierControls();
    }

    // ---- H-303: per-component height scale + fade (dynamic modifiers) ----

    /// <summary>Mirror the selected component's HeightScale/Fade into the controls.
    /// Suppresses handler re-entry while the sliders move programmatically.</summary>
    private void LoadModifierControls()
    {
        if (!_ready || Vm.Selected is not { } sel)
        {
            HeightScaleSlider.IsEnabled = TxtHeightScale.IsEnabled =
                FadeAmountSlider.IsEnabled = FadeDirectionPicker.IsEnabled = false;
            return;
        }

        HeightScaleSlider.IsEnabled = TxtHeightScale.IsEnabled =
            FadeAmountSlider.IsEnabled = FadeDirectionPicker.IsEnabled = true;

        _loadingModifiers = true;
        try
        {
            double scale = sel.HeightScale ?? 1.0;
            HeightScaleSlider.Value = Math.Clamp(scale, HeightScaleSlider.Minimum, HeightScaleSlider.Maximum);
            TxtHeightScale.Text = scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            FadeAmountSlider.Value = sel.FadeAmount ?? 0.0;
            FadeDirectionPicker.SelectedItem = sel.FadeDirection ?? FadeDirection.None;
        }
        finally { _loadingModifiers = false; }
    }

    private bool _loadingModifiers;

    /// <summary>True when any modifier actually changed the selection — lets tests
    /// and callers decide whether a recomposite is worth reporting.</summary>
    public bool LastModifierChanged { get; private set; }

    private void ComponentModifier_Changed(object sender, RoutedEventArgs e)
        => ComponentModifier_ChangedCore();

    // Slider overload: RoutedPropertyChangedEventArgs<double>.
    private void ComponentModifier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ComponentModifier_ChangedCore();

    private void ComponentModifier_ChangedCore()
    {
        if (!_ready || _loadingModifiers || Vm.Selected is not { } sel) return;

        _loadingModifiers = true;
        try
        {
            double slider = HeightScaleSlider.Value;
            string? text = TxtHeightScale.Text?.Trim();
            if (!string.IsNullOrEmpty(text) &&
                double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var typed))
            {
                slider = typed;   // the text box wins while it has content
            }
            sel.HeightScale = slider > 0 ? slider : null;

            var dir = FadeDirectionPicker.SelectedItem as FadeDirection? ?? FadeDirection.None;
            double amt = FadeAmountSlider.Value;
            sel.FadeDirection = dir == FadeDirection.None ? null : dir;
            sel.FadeAmount = dir == FadeDirection.None || amt <= 0 ? null : amt;

            // Keep the slider and the text box agreeing with what was stored.
            double shown = sel.HeightScale ?? 1.0;
            HeightScaleSlider.Value = Math.Clamp(shown, HeightScaleSlider.Minimum, HeightScaleSlider.Maximum);
            TxtHeightScale.Text = shown.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { _loadingModifiers = false; }

        Vm.Recomposite();
        Refresh();
        LastModifierChanged = true;
    }

    private void HeightScale_TextCommitted(object sender, RoutedEventArgs e)
        => ComponentModifier_ChangedCore();

    // TextBox.KeyDown fires KeyEventArgs (not RoutedEventArgs) — route it too.
    private void HeightScale_TextCommitted(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ComponentModifier_ChangedCore();
    }

    /// <summary>H-303 test seam (no InternalsVisibleTo): drive the exact code path
    /// the Height/Fade controls run, with explicit values instead of UI input.</summary>
    public void RaiseModifierChangedForTest(double? heightScale, double? fadeAmount, FadeDirection? fadeDirection)
    {
        _loadingModifiers = true;
        try
        {
            HeightScaleSlider.Value = Math.Clamp(heightScale ?? 1.0,
                HeightScaleSlider.Minimum, HeightScaleSlider.Maximum);
            TxtHeightScale.Text = (heightScale ?? 1.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            FadeAmountSlider.Value = fadeAmount ?? 0;
            FadeDirectionPicker.SelectedItem = fadeDirection ?? FadeDirection.None;
        }
        finally { _loadingModifiers = false; }
        ComponentModifier_ChangedCore();
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
