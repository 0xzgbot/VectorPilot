using System.Windows;

namespace VectorPilot.App.Controls;

/// <summary>Asks for a template name when saving the current Cut params.</summary>
public partial class TemplateNameDialog : Window
{
    public string TemplateName { get; private set; } = "";

    public TemplateNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtName.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Give the template a name.", "Save template",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        TemplateName = TxtName.Text.Trim();
        DialogResult = true;
    }
}
