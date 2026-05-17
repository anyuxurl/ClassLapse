using System.Runtime.Versioning;
using System.Windows;
using ClassLapse.Core;
using ClassLapse.Views;

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
        var config = configStore.Load();

        if (NeedsFirstRunSetup(config))
        {
            var wizard = new SettingsWindow(configStore, cameraService, isFirstRun: true);
            var completed = wizard.ShowDialog();
            if (completed != true)
            {
                Shutdown(0);
                return;
            }
        }

        var scheduler = new CaptureScheduler(configStore);
        _trayApp = new TrayApp(configStore, cameraService, scheduler);
        _trayApp.Start();
    }

    private static bool NeedsFirstRunSetup(Models.AppConfig config)
    {
        return string.IsNullOrWhiteSpace(config.Camera.DeviceMoniker)
            || string.IsNullOrWhiteSpace(config.Storage.OutputFolder);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        base.OnExit(e);
    }
}
