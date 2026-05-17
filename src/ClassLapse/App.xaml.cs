using System.Windows;
using ClassLapse.Core;

namespace ClassLapse;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0].StartsWith("--"))
        {
            int exitCode = await DevCli.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        // M3 will wire TrayApp here. For now the app starts and stays alive
        // (ShutdownMode=OnExplicitShutdown in App.xaml) without showing any window.
    }
}
