using System.Windows;
using System.Windows.Threading;

namespace ShortcutStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Shortcut Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
