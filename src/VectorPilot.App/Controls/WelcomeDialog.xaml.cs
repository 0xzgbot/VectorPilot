using System.Windows;
using Microsoft.Win32;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// First-run welcome (Mac SPK-UXPOLISH parity): orients a new user, states the
/// hardware-e-stop warning the safety rules require, and offers the three real
/// starting points.
/// </summary>
public partial class WelcomeDialog : Window
{
    /// <summary>Set when the user picked a starting action, so the caller can honour it.</summary>
    public string? ChosenAction { get; private set; }

    public bool SuppressFuture => ChkDontShow.IsChecked == true;

    public WelcomeDialog()
    {
        InitializeComponent();
    }

    private void Recipe_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = "recipe";
        Close();
    }

    private void Blank_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = "blank";
        Close();
    }
}
