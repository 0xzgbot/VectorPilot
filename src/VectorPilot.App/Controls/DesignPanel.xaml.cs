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
    private enum Tool { Select, Rectangle, Line, Circle }

    private VectorPoint? _dragStart;
    private Shape? _preview;
    private readonly List<UIElement> _shapeElements = new();

    public DesignPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitView();
        Loaded += (_, _) => Refresh();
    }

    public void RefreshIfVisible() => Refresh();

    private Tool CurrentTool =>
        ToolRect.IsChecked == true ? Tool.Rectangle :
        ToolLine.IsChecked == true ? Tool.Line :
        ToolCircle.IsChecked == true ? Tool.Circle : Tool.Select;

    private void Refresh()
    {
        var job = AppState.CurrentJob;
        if (job is null) return;
        LayerLabel.Text = $"{job.ActiveSheet.Name} · {job.ActiveSheet.ActiveLayer.Name} · {job.ActiveSheet.Layers.Count} layer(s)";
        FitView();
        RedrawShapes();
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

    private void RedrawShapes()
    {
        foreach (var el in _shapeElements) DrawCanvas.Children.Remove(el);
        _shapeElements.Clear();

        var job = AppState.CurrentJob;
        if (job is null) return;
        var sheet = job.ActiveSheet;

        // sheet boundary
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
            var brush = new SolidColorBrush(ToMediaColor(layer.Color));
            foreach (var shape in layer.Shapes)
            {
                var el = ShapeToElement(shape, brush, layer.Locked ? 0.6 : 1.0);
                if (el is not null) { DrawCanvas.Children.Add(el); _shapeElements.Add(el); }
            }
        }
    }

    private static UIElement? ShapeToElement(VectorShape shape, Brush brush, double opacity)
    {
        switch (shape.Type)
        {
            case ShapeType.Line when shape.Points.Count >= 2:
                return new Line
                {
                    X1 = shape.Points[0].X, Y1 = shape.Points[0].Y,
                    X2 = shape.Points[1].X, Y2 = shape.Points[1].Y,
                    Stroke = brush, StrokeThickness = 1.2, Opacity = opacity, IsHitTestVisible = false
                };
            case ShapeType.Rectangle when shape.Points.Count >= 2:
            {
                var b = shape.Bounds();
                return new Rectangle
                {
                    Width = b.Width, Height = b.Height,
                    Stroke = brush, StrokeThickness = 1.2, Opacity = opacity, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(b.MinX, b.MinY)
                };
            }
            case ShapeType.Circle when shape.Points.Count == 1:
            {
                var c = shape.Points[0];
                var el = new Ellipse
                {
                    Width = shape.Radius * 2, Height = shape.Radius * 2,
                    Stroke = brush, StrokeThickness = 1.2, Opacity = opacity, IsHitTestVisible = false,
                    RenderTransform = new TranslateTransform(c.X - shape.Radius, c.Y - shape.Radius)
                };
                return el;
            }
            case ShapeType.Polyline or ShapeType.Rectangle when shape.Points.Count >= 2:
            {
                var pg = new Polyline
                {
                    Stroke = brush, StrokeThickness = 1.2, Opacity = opacity, IsHitTestVisible = false
                };
                foreach (var p in shape.Points) pg.Points.Add(new Point(p.X, p.Y));
                if (shape.Closed) pg.Points.Add(new Point(shape.Points[0].X, shape.Points[0].Y));
                return pg;
            }
            default:
                return null;
        }
    }

    private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color c)
        => System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    private VectorPoint ScreenToWorld(Point p)
    {
        double scale = ViewScale.ScaleX == 0 ? 1 : ViewScale.ScaleX;
        return new VectorPoint((p.X - ViewOffset.X) / scale, (p.Y - ViewOffset.Y) / scale);
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CurrentTool == Tool.Select) return;
        _dragStart = ScreenToWorld(e.GetPosition(DrawCanvas));
        DrawCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;
        var cur = ScreenToWorld(e.GetPosition(DrawCanvas));
        if (_preview is not null) { DrawCanvas.Children.Remove(_preview); _preview = null; }

        var brush = new SolidColorBrush(Colors.Black);
        switch (CurrentTool)
        {
            case Tool.Rectangle:
            {
                double x = Math.Min(_dragStart.Value.X, cur.X), y = Math.Min(_dragStart.Value.Y, cur.Y);
                double w = Math.Abs(cur.X - _dragStart.Value.X), h = Math.Abs(cur.Y - _dragStart.Value.Y);
                _preview = new Rectangle { Width = w, Height = h, Stroke = brush, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false, RenderTransform = new TranslateTransform(x, y) };
                break;
            }
            case Tool.Line:
                _preview = new Line { X1 = _dragStart.Value.X, Y1 = _dragStart.Value.Y, X2 = cur.X, Y2 = cur.Y, Stroke = brush, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false };
                break;
            case Tool.Circle:
            {
                double r = _dragStart.Value.DistanceTo(cur);
                _preview = new Ellipse { Width = r * 2, Height = r * 2, Stroke = brush, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false, RenderTransform = new TranslateTransform(_dragStart.Value.X - r, _dragStart.Value.Y - r) };
                break;
            }
        }
        if (_preview is not null) DrawCanvas.Children.Add(_preview);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        DrawCanvas.ReleaseMouseCapture();
        var cur = ScreenToWorld(e.GetPosition(DrawCanvas));
        if (_preview is not null) { DrawCanvas.Children.Remove(_preview); _preview = null; }

        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null || layer.Locked) { _dragStart = null; return; }

        VectorShape? shape = null;
        switch (CurrentTool)
        {
            case Tool.Rectangle:
            {
                double x = Math.Min(_dragStart.Value.X, cur.X), y = Math.Min(_dragStart.Value.Y, cur.Y);
                double w = Math.Abs(cur.X - _dragStart.Value.X), h = Math.Abs(cur.Y - _dragStart.Value.Y);
                if (w > 0.01 && h > 0.01) shape = VectorShape.Rectangle(x, y, w, h);
                break;
            }
            case Tool.Line:
                if (_dragStart.Value.DistanceTo(cur) > 0.01) shape = VectorShape.Line(_dragStart.Value, cur);
                break;
            case Tool.Circle:
            {
                double r = _dragStart.Value.DistanceTo(cur);
                if (r > 0.01) shape = VectorShape.Circle(_dragStart.Value, r);
                break;
            }
        }

        _dragStart = null;
        if (shape is not null && AppState.CurrentJob is { } job)
        {
            layer.AddShape(shape);
            job.IsDirty = true;
            RedrawShapes();
        }
    }
}
