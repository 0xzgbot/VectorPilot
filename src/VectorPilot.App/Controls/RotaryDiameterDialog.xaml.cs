using System.Windows;

namespace VectorPilot.App.Controls;

/// <summary>H-403: one-field prompt for the rotary stock diameter.</summary>
public partial class RotaryDiameterDialog : Window
{
    public double DiameterMm { get; private set; }

    public RotaryDiameterDialog(double current)
    {
        InitializeComponent();
        TxtDiameter.Text = current.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        TxtDiameter.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtDiameter.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 1)
        {
            System.Windows.MessageBox.Show("Enter a positive diameter in mm.", "VectorPilot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DiameterMm = v;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
