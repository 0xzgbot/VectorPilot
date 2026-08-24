using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class DesignPanel
{
    internal void RedrawShapes()
    {
        foreach (var el in _shapeElements) DrawCanvas.Children.Remove(el);
        _shapeElements.Clear();

        var job = AppState.CurrentJob;
        if (job is null) return;
        var sheet = job.ActiveSheet;

        var sheetRect = new Rectangle
        {
            Width = sheet.Width, Height = sheet.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF5)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(sheetRect, 0); Canvas.SetTop(sheetRect, 0);
        DrawCanvas.Children.Add(sheetRect); _shapeElements.Add(sheetRect);

        foreach (var layer in sheet.Layers)
        {
            if (!layer.Visible) continue;
            var brush = new SolidColorBrush(Color.FromArgb(layer.Color.A, layer.Color.R, layer.Color.G, layer.Color.B));
            foreach (var shape in layer.Shapes)
            {
                bool selected = Selection.IsSelected(shape);
                bool followed = AppState.FollowedSourceShapeIds.Contains(shape.Id);
                var stroke = selected ? Brushes.OrangeRed
                           : followed ? Brushes.DodgerBlue      // card P2: cut by the selected toolpath
                           : brush;
                var el = ShapeToElement(shape, stroke, layer.Locked ? 0.6 : 1.0, selected || followed);
                if (el is not null) { DrawCanvas.Children.Add(el); _shapeElements.Add(el); }
            }
        }

        DrawSelectionBounds();
        DrawNodeHandles();
        DrawKeepOutZones();
        DrawToolpathOverlay();   // P-101: calculated G-code strokes (when toggled)
    }

    // ---- P-101: calculated toolpath overlay ----

    /// <summary>True when the "Show toolpaths" toggle is checked. Public seam so
    /// tests read the same state the checkbox drives.</summary>
    public bool ShowToolpaths => ChkShowToolpaths?.IsChecked == true;

    private readonly List<UIElement> _toolpathElements = new();

    private void ShowToolpaths_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;   // XAML init fires the events before the canvas exists in a usable state
        RedrawShapes();
    }

    /// <summary>
    /// P-101: paint every calculated toolpath's G1 path onto the sheet in world mm —
    /// cut moves as solid green strokes, rapids (G0) as thin dashed red, so you can
    /// see whether the bit stays inside the shape without streaming anything.
    /// Empty/not-calculated toolpaths paint nothing.
    /// </summary>
    private void DrawToolpathOverlay()
    {
        foreach (var el in _toolpathElements) DrawCanvas.Children.Remove(el);
        _toolpathElements.Clear();
        if (!ShowToolpaths) return;

        foreach (var tp in AppState.Toolpaths.Toolpaths)
        {
            if (tp.GCode.Count == 0) continue;

            var segments = WireframeRenderer.GenerateSegments(tp.GCode);
            foreach (var seg in segments)
            {
                var line = new Line
                {
                    X1 = seg.Start.X, Y1 = seg.Start.Y,
                    X2 = seg.End.X, Y2 = seg.End.Y,
                    Stroke = seg.IsRapid ? Brushes.Red : Brushes.Green,
                    StrokeThickness = Math.Max(WorldTolerance(seg.IsRapid ? 0.8 : 1.4), 0.05),
                    Opacity = seg.IsRapid ? 0.55 : 0.9,
                    StrokeDashArray = seg.IsRapid ? new DoubleCollection { 3, 2 } : null,
                    IsHitTestVisible = false
                };
                DrawCanvas.Children.Add(line);
                _toolpathElements.Add(line);
            }
        }
    }

    /// <summary>Card P3: hatched red overlay for no-cut zones.</summary>
    private void DrawKeepOutZones()
    {
        foreach (var z in AppState.CurrentJob.KeepOutZones)
        {
            if (!z.IsActive || z.Type != KeepOutZoneType.Rectangle) continue;
            if (z.RectMinX is not { } x0 || z.RectMinY is not { } y0 ||
                z.RectMaxX is not { } x1 || z.RectMaxY is not { } y1) continue;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Abs(x1 - x0),
                Height = Math.Abs(y1 - y0),
                Stroke = System.Windows.Media.Brushes.Firebrick,
                StrokeThickness = 1.5,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 3 },
                Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(40, 178, 34, 34)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, Math.Min(x0, x1));
            Canvas.SetTop(rect, Math.Min(y0, y1));
            DrawCanvas.Children.Add(rect);
            _shapeElements.Add(rect);
        }
    }

    /// <summary>Card A1: draw draggable point handles while node-editing.</summary>
    private void DrawNodeHandles()
    {
        if (!NodeEdit.IsActive) return;
        double r = Math.Max(WorldTolerance(4), 0.15);

        var handles = NodeEdit.Handles;
        for (int i = 0; i < handles.Count; i++)
        {
            bool isSelected = i == NodeEdit.SelectedNode;
            var dot = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = isSelected ? Brushes.OrangeRed : Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = Math.Max(WorldTolerance(1), 0.04),
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(handles[i].X - r, handles[i].Y - r)
            };
            DrawCanvas.Children.Add(dot);
            _shapeElements.Add(dot);
        }
    }

    private void DrawSelectionBounds()
    {
        var b = Selection.SelectionBounds();
        if (b is null) return;
        var box = new Rectangle
        {
            Width = Math.Max(b.Value.MaxX - b.Value.MinX, 0.01),
            Height = Math.Max(b.Value.MaxY - b.Value.MinY, 0.01),
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = Math.Max(WorldTolerance(1), 0.05),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(b.Value.MinX, b.Value.MinY)
        };
        DrawCanvas.Children.Add(box); _shapeElements.Add(box);
    }

    private UIElement? ShapeToElement(VectorShape shape, Brush brush, double opacity, bool selected)
    {
        double thickness = Math.Max(WorldTolerance(selected ? 2.0 : 1.2), 0.05);
        switch (shape.Type)
        {
            case ShapeType.Line when shape.Points.Count >= 2:
                return new Line
                {
                    X1 = shape.Points[0].X, Y1 = shape.Points[0].Y,
                    X2 = shape.Points[1].X, Y2 = shape.Points[1].Y,
                    Stroke = brush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false
                };

            case ShapeType.Circle when shape.Points.Count == 1:
            {
                var c = shape.Points[0];
                return new Ellipse
                {
                    Width = shape.Radius * 2, Height = shape.Radius * 2,
                    Stroke = brush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(c.X - shape.Radius, c.Y - shape.Radius)
                };
            }

            case ShapeType.Rectangle or ShapeType.Polyline when shape.Points.Count >= 2:
            {
                var pl = new Polyline
                {
                    Stroke = brush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false
                };
                foreach (var p in shape.Points) pl.Points.Add(new Point(p.X, p.Y));
                if (shape.Closed && shape.Points.Count > 2)
                    pl.Points.Add(new Point(shape.Points[0].X, shape.Points[0].Y));
                return pl;
            }

            default:
                return null;
        }
    }
}
