using System.Globalization;
using System.Windows;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

/// <summary>Card A3: exact numeric transform dialog for the canvas selection.</summary>
public partial class TransformDialog : Window
{
    private readonly IReadOnlyList<VectorShape> _shapes;

    /// <summary>True when Apply mutated the selection.</summary>
    public bool Applied { get; private set; }

    public TransformDialog(IReadOnlyList<VectorShape> shapes)
    {
        InitializeComponent();
        _shapes = shapes;

        var b = TransformOps.Bounds(shapes);
        if (b is not null)
        {
            PosX.Text = F(b.Value.MinX);
            PosY.Text = F(b.Value.MinY);
            SizeW.Text = F(b.Value.MaxX - b.Value.MinX);
            SizeH.Text = F(b.Value.MaxY - b.Value.MinY);
        }
        InfoLabel.Text = $"{shapes.Count} shape(s) selected";
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryNum(string text, out double value)
        => double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";

        if (!TryNum(PosX.Text, out double x) || !TryNum(PosY.Text, out double y) ||
            !TryNum(SizeW.Text, out double w) || !TryNum(SizeH.Text, out double h) ||
            !TryNum(Angle.Text, out double angle) || !TryNum(Factor.Text, out double factor))
        {
            ErrorLabel.Text = "Every field must be a number.";
            return;
        }
        if (w <= 0 || h <= 0) { ErrorLabel.Text = "Width and height must be positive."; return; }
        if (factor <= 0) { ErrorLabel.Text = "Scale factor must be positive."; return; }

        // Order: size → position → rotate → scale.
        TransformOps.SetSize(_shapes, w, h, LockAspect.IsChecked == true);
        TransformOps.SetPosition(_shapes, x, y);
        if (Math.Abs(angle) > 1e-9) TransformOps.RotateBy(_shapes, angle);
        if (Math.Abs(factor - 1.0) > 1e-9) TransformOps.ScaleBy(_shapes, factor);

        Applied = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
