using System.Runtime.Versioning;
using System.Windows;
using ClassLapse.Core;

namespace ClassLapse;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private TrayApp? _trayApp;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0].StartsWith("--"))
        {
            int exitCode = await DevCli.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        var configStore = new ConfigStore();
        var cameraService = new CameraService();
        var scheduler = new CaptureScheduler(configStore);
        _trayApp = new TrayApp(configStore, cameraService, scheduler);
        _trayApp.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        base.OnExit(e);
    }
}
