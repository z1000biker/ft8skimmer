using System.Windows;

namespace Skimmer.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ev) =>
        {
            MessageBox.Show(ev.Exception.Message, "RtlFt8Skimmer", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };
        base.OnStartup(e);
    }
}
