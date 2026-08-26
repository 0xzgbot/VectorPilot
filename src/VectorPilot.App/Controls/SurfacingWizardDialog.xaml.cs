using System.Windows;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// H-402: wasteboard surfacing wizard . Generates a raster facing
/// program for the sheet XY and lands it in the Cuts list as a real Toolpath row.
/// The user must press Start themselves — nothing here streams to the machine.
/// </summary>
public partial class SurfacingWizardDialog : Window
{
    /// <summary>The toolpath this dialog created, after GenerateAndLand succeeds.
    /// Public: the test project has no InternalsVisibleTo.</summary>
    public Toolpath? Created { get; private set; }

    private readonly MachineDock _dock;

    public SurfacingWizardDialog(MachineDock dock)
    {
        InitializeComponent();
        _dock = dock;

        var sheet = AppState.CurrentJob.ActiveSheet;
        double w = ParseDim(sheet.Width, 600), h = ParseDim(sheet.Height, 400);
        TxtSheetW.Text = w.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        TxtSheetH.Text = h.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Generate from the current fields and land the program as a Cuts-list toolpath.
    /// Returns the created toolpath (also on <see cref="Created"/>) or null when the
    /// input is unusable. This is the same path the Generate button invokes.
    /// </summary>
    public Toolpath? GenerateAndLand()
    {
        double w = Parse(TxtSheetW.Text), h = Parse(TxtSheetH.Text);
        if (w <= 0 || h <= 0)
        {
            ResultLabel.Text = "Sheet size must be positive mm.";
            return null;
        }

        var result = WasteboardSurfacing.Generate(new WasteboardSurfacing.Params
        {
            SheetWidthMm = w,
            SheetHeightMm = h,
            CutterDiameterMm = Parse(TxtCutter.Text, 22),
            StepoverPercent = Parse(TxtStepover.Text, 40),
            DepthPerPassMm = Parse(TxtDepth.Text, 1),
            FeedRateMmPerMin = Parse(TxtFeed.Text, 800),
            SpindleRpm = Parse(TxtRpm.Text, 18000),
        });

        // Land as a REAL Cuts row — visible in the operations list, editable like any
        // other program. StrategyKey left null: recalculation is not meaningful here.
        var tp = AppState.Toolpaths.Add(ToolpathStrategy.Rough3D,
            name: $"Wasteboard surfacing ({result.RowCount} rows)");
        tp.SetResult(result.GcodeLines);
        tp.EstimatedTimeSeconds =
            result.PathLengthMm / Math.Max(1, Parse(TxtFeed.Text, 800)) * 60 * 1.4;   // +Z lifts

        RefreshCutsList();

        ResultLabel.Text = $"✔ {result.RowCount} raster rows · ~{tp.EstimatedTimeSeconds / 60:0.#} min · " +
                           "landed in Cuts. Press Start on the Machine stage when ready.";
        Created = tp;
        return tp;
    }

    /// <summary>Ask the shell's Cuts list to rebuild so the new row shows immediately.
    /// Application.MainWindow's getter itself is thread-affine — a caller on any other
    /// thread (stale shell, tests) just skips the cosmetic refresh.</summary>
    private void RefreshCutsList()
    {
        try
        {
            if (Application.Current?.MainWindow is MainWindow mw) mw.RefreshCutsFromWizard();
        }
        catch (InvalidOperationException)
        {
            // Wrong thread / no shell — the Cuts list rebuilds on its next refresh anyway.
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => GenerateAndLand();

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private static double Parse(string? text, double fallback = 0)
        => double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static double ParseDim(object? value, double fallback)
        => value switch
        {
            double d => d,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
            _ => fallback
        };
}
