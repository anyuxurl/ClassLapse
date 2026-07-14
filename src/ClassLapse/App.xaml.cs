using System.Runtime.Versioning;
using System.Windows;
using ClassLapse.Core;
using ClassLapse.Views;

namespace ClassLapse;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private TrayApp? _trayApp;
    private ProcessWatchdog? _watchdog;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (ProcessWatchdog.IsInvocation(e.Args))
        {
            int exitCode = await ProcessWatchdog.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].StartsWith("--"))
        {
            int exitCode = await DevCli.RunAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        if (!TryAcquireSingleInstance())
        {
            Log.Info("another ClassLapse instance is already running; exiting");
            Shutdown(0);
            return;
        }

        Log.Info($"ClassLapse starting (PID {Environment.ProcessId})");
        _watchdog = ProcessWatchdog.TryStartForCurrentProcess();

        var configStore = new ConfigStore();
        configStore.MigrateOnDiskIfNeeded();
        var cameraService = new CameraService();
        var config = configStore.Load();

        if (NeedsFirstRunSetup(config))
        {
            Log.Info("first-run setup required (missing camera or output folder)");
            var wizard = new SettingsWindow(configStore, cameraService, isFirstRun: true);
            var completed = wizard.ShowDialog();
            if (completed != true)
            {
                Log.Info("first-run cancelled by user, exiting");
                Shutdown(0);
                return;
            }
        }

        var scheduler = new CaptureScheduler(configStore);
        _trayApp = new TrayApp(configStore, cameraService, scheduler);
        _trayApp.Start();
        Log.Info("scheduler started, tray ready");
    }

    private static bool NeedsFirstRunSetup(Models.AppConfig config)
    {
        return string.IsNullOrWhiteSpace(config.Camera.DeviceMoniker)
            || string.IsNullOrWhiteSpace(config.Storage.OutputFolder);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watchdog?.MarkCleanExit();
        Log.Info("ClassLapse exiting");
        _trayApp?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { /* already released */ }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\ClassLapse.SingleInstance",
                createdNew: out bool createdNew);
            _ownsSingleInstanceMutex = createdNew;
            return createdNew;
        }
        catch (Exception ex)
        {
            Log.Warn("single-instance guard unavailable: " + ex.Message);
            return true;
        }
    }
}
