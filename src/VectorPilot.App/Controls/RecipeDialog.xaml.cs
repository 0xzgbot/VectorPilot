using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// Card P1: recipe picker. Builds a complete job from a recipe rather than an
/// empty sheet — the carved-sign recipe wires real extracted glyphs into
/// <see cref="SignRecipeManager"/>, which previously had no UI at all.
/// </summary>
public partial class RecipeDialog : Window
{
    /// <summary>The job the user chose to create; null if cancelled.</summary>
    public Job? CreatedJob { get; private set; }

    public RecipeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            CmbFont.ItemsSource = GlyphExtractor.AvailableFonts();
            CmbFont.SelectedItem = CmbFont.Items.Contains("Segoe UI") ? "Segoe UI" : CmbFont.Items[0];
            RecipeList.SelectionChanged += (_, _) => UpdateEnabled();
            UpdateEnabled();
        };
    }

    private string SelectedRecipe =>
        (RecipeList.SelectedItem as ListBoxItem)?.Tag as string ?? "sign";

    private void UpdateEnabled()
    {
        bool sign = SelectedRecipe == "sign";
        TxtSignText.IsEnabled = CmbFont.IsEnabled = TxtAngle.IsEnabled = TxtDepth.IsEnabled = sign;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedRecipe == "blank")
            {
                CreatedJob = Job.CreateEmpty();
                DialogResult = true;
                return;
            }

            string text = TxtSignText.Text.Trim();
            if (text.Length == 0) { Note.Text = "Enter some sign text."; return; }

            double angle = Parse(TxtAngle.Text, 90);
            double depth = Parse(TxtDepth.Text, 3.0);
            if (angle <= 0 || angle >= 180) { Note.Text = "V-bit angle must be between 0 and 180°."; return; }
            if (depth <= 0) { Note.Text = "Carve depth must be positive."; return; }

            // Real outlines, not placeholder boxes.
            var glyphs = GlyphExtractor.Extract(text, CmbFont.SelectedItem as string ?? "Segoe UI", fontSize: 96);

            CreatedJob = SignRecipeManager.CreateSignJob(
                jobName: $"Sign — {text}",
                text: text,
                glyphs: glyphs,
                vBitAngle: angle,
                vCarveDepth: depth);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            Note.Text = $"Could not build the recipe: {ex.Message}";
        }
    }

    private static double Parse(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
