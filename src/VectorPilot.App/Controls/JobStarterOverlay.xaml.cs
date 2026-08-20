using System.Windows;
using System.Windows.Controls;

namespace VectorPilot.App.Controls;

/// <summary>
/// The first thing a new user sees: three concrete jobs instead of a 28-entry strategy combo.
///
/// Each starter sets <see cref="AppState.UiMode"/> and the strategy the job should begin on,
/// then raises <see cref="Started"/> so MainWindow can dismiss the overlay and refresh the Cut
/// panel. The handlers delegate to <see cref="ApplyStarter"/>, which is what the tests call —
/// so a test cannot pass while the button is dead.
/// </summary>
public partial class JobStarterOverlay : UserControl
{
    /// <summary>Raised when a starter was chosen (or skipped), with the strategy to select.</summary>
    public event Action<JobStarterKind?, string?>? Started;

    public JobStarterOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The strategy key this starter begins on. Pure lookup — the mode is decided downstream
    /// by CutPanel.SelectStrategy, so there is one owner of that rule instead of two.
    /// </summary>
    public static string ApplyStarter(JobStarterKind kind) => UiModeCatalog.StarterStrategy(kind);

    private void Choose(JobStarterKind kind)
    {
        var key = ApplyStarter(kind);
        StarterNote.Text = $"{UiModeCatalog.Label(kind)} selected — starting you on this operation.";
        Started?.Invoke(kind, key);
    }

    private void StarterSign_Click(object sender, RoutedEventArgs e) => Choose(JobStarterKind.Sign);

    private void StarterPhoto_Click(object sender, RoutedEventArgs e) => Choose(JobStarterKind.Photo);

    private void Starter3D_Click(object sender, RoutedEventArgs e) => Choose(JobStarterKind.ThreeD);

    private void StarterSkip_Click(object sender, RoutedEventArgs e)
    {
        // Skipping is an explicit request for the full tool set.
        AppState.UiMode = UiMode.Advanced;
        Started?.Invoke(null, null);
    }
}
