using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class SetupPanel : UserControl
{
    public event Action? JobCreated;

    public SetupPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshFromJob();
    }

    private void RefreshFromJob()
    {
        var job = AppState.CurrentJob;
        var sheet = job.ActiveSheet;
        TxtWidth.Text = sheet.Width.ToString("F2", CultureInfo.InvariantCulture);
        TxtHeight.Text = sheet.Height.ToString("F2", CultureInfo.InvariantCulture);
        TxtThickness.Text = sheet.Thickness.ToString("F3", CultureInfo.InvariantCulture);
        RbInches.IsChecked = sheet.Units == UnitSystem.Inches;
        RbMm.IsChecked = sheet.Units == UnitSystem.Millimeters;
        if (sheet.Material != null)
        {
            int i = CmbMaterial.Items.OfType<ComboBoxItem>().ToList().FindIndex(c => (c.Content as string) == sheet.Material.Name);
            if (i >= 0) CmbMaterial.SelectedIndex = i;
        }
    }

    /// <summary>Reveal the flip options only for a two-sided job.</summary>
    private void JobType_Changed(object sender, RoutedEventArgs e)
    {
        if (DualSidedOptions is null) return;   // fires during XAML init

        bool dual = RbDouble.IsChecked == true;
        DualSidedOptions.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;

        if (dual)
        {
            DualSidedNote.Text =
                "The back face is mirrored automatically. You will be prompted to turn the " +
                "stock over and re-zero Z between the two programs.";
        }
    }

    private void BtnCreateJob_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(TxtHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ||
            !double.TryParse(TxtThickness.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ||
            w <= 0 || h <= 0 || t <= 0)
        {
            MessageBox.Show("Enter valid positive dimensions.", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var units = RbMm.IsChecked == true ? UnitSystem.Millimeters : UnitSystem.Inches;
        var material = (CmbMaterial.SelectedItem as ComboBoxItem)?.Content as string ?? "Generic";
        AppState.NewJob(w, h, t, units, material);
        var job = AppState.CurrentJob;
        job.IsDoubleSided = RbDouble.IsChecked == true;
        job.IsRotary = RbRotary.IsChecked == true;

        if (job.IsDoubleSided)
        {
            job.FlipAxis = RbFlipHorizontal.IsChecked == true
                ? FlipAxis.Horizontal
                : FlipAxis.Vertical;

            if (ChkRegistrationHoles.IsChecked == true)
            {
                job.RegistrationHoles.Clear();
                job.RegistrationHoles.AddRange(
                    DualSidedMachining.RegistrationHoles(w, h, job.FlipAxis));
            }
        }

        JobCreated?.Invoke();
        MessageBox.Show($"Job created: {w:0.##} x {h:0.##} x {t:0.###} {UnitConversions.Suffix(units)}\n{job.ActiveSheet.Layers.Count} layer(s). Switch to Design to draw.", "VectorPilot");
    }
}
