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
                var el = ShapeToElement(shape, selected ? Brushes.OrangeRed : brush, layer.Locked ? 0.6 : 1.0, selected);
                if (el is not null) { DrawCanvas.Children.Add(el); _shapeElements.Add(el); }
            }
        }

        DrawSelectionBounds();
        DrawNodeHandles();
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
