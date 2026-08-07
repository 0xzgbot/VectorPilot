using System.Windows;

namespace VectorPilot.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"VectorPilot error:\n{args.Exception.Message}", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
