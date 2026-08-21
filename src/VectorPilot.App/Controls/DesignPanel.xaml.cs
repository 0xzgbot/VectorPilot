using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class DesignPanel : UserControl
{
    internal enum Tool { Select, Rectangle, Line, Circle, Polyline, Node }

    private VectorPoint? _dragStart;
    private Shape? _preview;
    private readonly List<UIElement> _shapeElements = new();
    private readonly List<VectorPoint> _polylinePoints = new();

    internal readonly UndoStack Undo = new();
    internal readonly SelectionModel Selection = new();

    private bool _movingSelection;
    private VectorPoint _moveLast;
    private List<VectorShape>? _pendingBefore;
    private bool _draggingNode;
    private List<VectorShape>? _nodeDragBefore;

    public DesignPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitView();
        Loaded += (_, _) => { Refresh(); RefreshViewPresets(); Focus(); };
        KeyDown += DesignPanel_KeyDown;
        // Card P2: repaint when the Toolpaths stage changes which shapes are followed.
        AppState.FollowedSourceChanged += () =>
        {
            if (IsLoaded) Dispatcher.BeginInvoke(new Action(RedrawShapes));
        };
    }

    public void RefreshIfVisible() => Refresh();

    internal Tool CurrentTool =>
        ToolRect.IsChecked == true ? Tool.Rectangle :
        ToolLine.IsChecked == true ? Tool.Line :
        ToolCircle.IsChecked == true ? Tool.Circle :
        ToolPolyline.IsChecked == true ? Tool.Polyline :
        ToolNode.IsChecked == true ? Tool.Node : Tool.Select;

    internal readonly NodeEditSession NodeEdit = new();

    public Layer? ActiveLayer => AppState.CurrentJob?.ActiveSheet.ActiveLayer;

    private void Refresh()
    {
        var job = AppState.CurrentJob;
        if (job is null) return;
        LayerLabel.Text = $"{job.ActiveSheet.Name} · {job.ActiveSheet.ActiveLayer.Name} · {job.ActiveSheet.Layers.Count} layer(s)";
        RefreshLayers();
        FitView();
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void UpdateEditChrome()
    {
        UndoButton.IsEnabled = Undo.CanUndo;
        RedoButton.IsEnabled = Undo.CanRedo;
        UndoButton.Content = Undo.CanUndo ? $"↶ {Undo.NextUndoLabel}" : "↶ Undo";
        RedoButton.Content = Undo.CanRedo ? $"↷ {Undo.NextRedoLabel}" : "↷ Redo";
        SelectionLabel.Text = Selection.IsEmpty ? "" : $"{Selection.Count} selected";

        bool canBool = BooleanSelectionOps.CanApply(Selection.Selected);
        UnionButton.IsEnabled = SubtractButton.IsEnabled = IntersectButton.IsEnabled = canBool;
    }

    internal void SetStatus(string text) => StatusLabel.Text = text;

    private void RefreshLayers()
    {
        var sheet = AppState.CurrentJob?.ActiveSheet;
        if (sheet is null) return;
        LayersList.ItemsSource = null;
        LayersList.ItemsSource = sheet.Layers;
        LayersList.SelectedItem = sheet.ActiveLayer;
    }

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppState.CurrentJob?.ActiveSheet is { } sheet && LayersList.SelectedItem is Layer layer)
        {
            sheet.ActiveLayer = layer;
            Selection.Clear();
            LayerLabel.Text = $"{sheet.Name} · {layer.Name} · {sheet.Layers.Count} layer(s)";
            RedrawShapes();
            UpdateEditChrome();
        }
    }

    private void LayerToggle_Changed(object sender, RoutedEventArgs e) => RedrawShapes();

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var sheet = AppState.CurrentJob?.ActiveSheet;
        if (sheet is null) return;
        var layer = sheet.AddLayer($"Layer {sheet.Layers.Count + 1}");
        sheet.ActiveLayer = layer;
        Refresh();
    }

    private void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        var sheet = AppState.CurrentJob?.ActiveSheet;
        if (sheet is null || sheet.Layers.Count <= 1) return;
        var layer = LayersList.SelectedItem as Layer ?? sheet.ActiveLayer;
        sheet.Layers.Remove(layer);
        if (sheet.ActiveLayer == layer) sheet.ActiveLayer = sheet.Layers[^1];
        Selection.Clear();
        Refresh();
    }

    private void Fit_Click(object sender, RoutedEventArgs e) { FitView(); RedrawShapes(); }

    /// <summary>Layer solo/isolate (Mac SPK-UXPOLISH parity).</summary>
    internal readonly LayerVisibilityModel LayerVisibility = new();

    private void LayerSolo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string layerName) return;

        var sheet = AppState.CurrentJob.ActiveSheet;
        bool soloed = LayerVisibility.ToggleSolo(sheet, layerName);

        SetStatus(soloed ? $"Solo: {layerName}" : "Solo cleared — visibility restored");
        RefreshLayers();
        RedrawShapes();
    }

    /// <summary>Named view presets (Mac SPK-UXPOLISH parity).</summary>
    internal readonly ViewPresetModel ViewPresets = new();

    private bool _applyingPreset;

    private void ViewPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset) return;
        if (CmbViewPreset.SelectedItem is not string name) return;
        if (ViewPresets.Find(name) is not { } preset) return;

        var sheet = AppState.CurrentJob.ActiveSheet;
        double zoom = ViewPresetModel.ResolveZoom(
            preset, DrawCanvas.ActualWidth, DrawCanvas.ActualHeight, sheet.Width, sheet.Height);

        if (preset.FitToSheet)
        {
            FitView();
        }
        else
        {
            ViewScale.ScaleX = ViewScale.ScaleY = zoom;
            ViewOffset.X = (DrawCanvas.ActualWidth - sheet.Width * zoom) / 2;
            ViewOffset.Y = (DrawCanvas.ActualHeight - sheet.Height * zoom) / 2;
        }

        SetStatus($"View: {preset.Name} ({ViewScale.ScaleX:P0})");
        RedrawShapes();
    }

    /// <summary>Populate the preset list without re-triggering the handler.</summary>
    internal void RefreshViewPresets()
    {
        _applyingPreset = true;
        CmbViewPreset.ItemsSource = ViewPresets.Presets.Select(p => p.Name).ToList();
        CmbViewPreset.SelectedIndex = 0;
        _applyingPreset = false;
    }

    private void FitView()
    {
        var job = AppState.CurrentJob;
        if (job is null || DrawCanvas.ActualWidth < 10) return;
        var sheet = job.ActiveSheet;
        double pad = 30;
        double scale = Math.Min((DrawCanvas.ActualWidth - pad * 2) / sheet.Width,
                                (DrawCanvas.ActualHeight - pad * 2) / sheet.Height);
        scale = Math.Max(scale, 0.001);
        ViewScale.ScaleX = ViewScale.ScaleY = scale;
        ViewOffset.X = (DrawCanvas.ActualWidth - sheet.Width * scale) / 2;
        ViewOffset.Y = (DrawCanvas.ActualHeight - sheet.Height * scale) / 2;
    }

    internal VectorPoint ScreenToWorld(Point p)
    {
        double scale = ViewScale.ScaleX == 0 ? 1 : ViewScale.ScaleX;
        return new VectorPoint((p.X - ViewOffset.X) / scale, (p.Y - ViewOffset.Y) / scale);
    }

    internal double WorldTolerance(double screenPixels)
    {
        double scale = ViewScale.ScaleX == 0 ? 1 : ViewScale.ScaleX;
        return screenPixels / scale;
    }

    private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color c)
        => System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
}
