using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClassLapse.Core;
using ClassLapse.Models;
using H.NotifyIcon;

namespace ClassLapse;

[SupportedOSPlatform("windows")]
public sealed class TrayApp : IDisposable
{
    private enum IconState { Green, Yellow, Red }

    private static readonly TimeSpan StickyBusyDuration = TimeSpan.FromMinutes(5);

    private readonly TaskbarIcon _tray;
    private readonly ConfigStore _configStore;
    private readonly CameraService _cameraService;
    private readonly CaptureScheduler _scheduler;

    private DateTime _todayStartedAt = DateTime.Today;
    private int _todayCount;
    private IconState _currentState = IconState.Green;
    private DateTime? _stickyBusyUntil;

    private MenuItem? _statusItem;
    private MenuItem? _todayItem;
    private MenuItem? _cameraItem;

    public TrayApp(ConfigStore configStore, CameraService cameraService, CaptureScheduler scheduler)
    {
        _configStore = configStore;
        _cameraService = cameraService;
        _scheduler = scheduler;

        _tray = new TaskbarIcon
        {
            ToolTipText = "ClassLapse · 课堂延时",
            ContextMenu = BuildContextMenu(),
        };
        ApplyIconState(IconState.Green);
        _tray.ForceCreate();

        _scheduler.OnTickEvaluated += OnTickEvaluated;
        _scheduler.OnCaptureRequested += OnCaptureRequestedAsync;
    }

    public void Start() => _scheduler.Start();

    public void Dispose()
    {
        _scheduler.OnTickEvaluated -= OnTickEvaluated;
        _scheduler.OnCaptureRequested -= OnCaptureRequestedAsync;
        _scheduler.Stop();
        _scheduler.Dispose();
        _tray.Dispose();
    }

    private void ApplyIconState(IconState state)
    {
        _currentState = state;
        Brush bg = state switch
        {
            IconState.Green => new SolidColorBrush(Color.FromRgb(46, 160, 67)),
            IconState.Yellow => new SolidColorBrush(Color.FromRgb(210, 153, 34)),
            IconState.Red => new SolidColorBrush(Color.FromRgb(218, 54, 51)),
            _ => Brushes.Gray,
        };
        _tray.IconSource = new GeneratedIconSource
        {
            Text = "CL",
            Foreground = Brushes.White,
            Background = bg,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        _statusItem = new MenuItem { Header = "ClassLapse · 启动中", IsEnabled = false };
        menu.Items.Add(_statusItem);

        _todayItem = new MenuItem { Header = "今日已拍: 0 张", IsEnabled = false };
        menu.Items.Add(_todayItem);

        _cameraItem = new MenuItem { Header = "摄像头: 未配置", IsEnabled = false };
        menu.Items.Add(_cameraItem);

        menu.Items.Add(new Separator());

        var pause1h = new MenuItem { Header = "⏸  暂停 1 小时" };
        pause1h.Click += (_, _) => PauseFor(TimeSpan.FromHours(1));
        menu.Items.Add(pause1h);

        var pauseToday = new MenuItem { Header = "⏸  暂停今天剩余" };
        pauseToday.Click += (_, _) => PauseUntilEndOfDay();
        menu.Items.Add(pauseToday);

        var resume = new MenuItem { Header = "▶  恢复" };
        resume.Click += (_, _) => Resume();
        menu.Items.Add(resume);

        menu.Items.Add(new Separator());

        var captureNow = new MenuItem { Header = "📷  立即拍一张（测试）" };
        captureNow.Click += async (_, _) => await TriggerManualCaptureAsync();
        menu.Items.Add(captureNow);

        var openFolder = new MenuItem { Header = "📂  打开输出文件夹" };
        openFolder.Click += (_, _) => OpenOutputFolder();
        menu.Items.Add(openFolder);

        var openConfig = new MenuItem { Header = "⚙️  打开配置文件" };
        openConfig.Click += (_, _) => OpenConfigFile();
        menu.Items.Add(openConfig);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    private void OnTickEvaluated(ScheduleDecision.Reason reason, AppConfig config)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DateTime.Today > _todayStartedAt)
            {
                _todayStartedAt = DateTime.Today;
                _todayCount = 0;
            }

            IconState target = ResolveIconState(reason);
            if (target != _currentState) ApplyIconState(target);

            UpdateMenuLines(reason, config);
        }));
    }

    private IconState ResolveIconState(ScheduleDecision.Reason reason)
    {
        if (_stickyBusyUntil.HasValue)
        {
            if (_stickyBusyUntil.Value > DateTime.Now) return IconState.Yellow;
            _stickyBusyUntil = null;
        }

        return reason switch
        {
            ScheduleDecision.Reason.Paused => IconState.Yellow,
            _ => IconState.Green,
        };
    }

    private void UpdateMenuLines(ScheduleDecision.Reason reason, AppConfig config)
    {
        if (_statusItem != null)
        {
            _statusItem.Header = "ClassLapse · " + ReasonToZh(reason, config);
        }
        if (_todayItem != null)
        {
            _todayItem.Header = $"今日已拍: {_todayCount} 张";
        }
        if (_cameraItem != null)
        {
            _cameraItem.Header = string.IsNullOrWhiteSpace(config.Camera.FriendlyName)
                ? "摄像头: 未配置"
                : $"摄像头: {config.Camera.FriendlyName}";
        }
    }

    private static string ReasonToZh(ScheduleDecision.Reason reason, AppConfig config) => reason switch
    {
        ScheduleDecision.Reason.ShouldCapture => "运行中",
        ScheduleDecision.Reason.OutsideActiveDay => "今天不在计划内",
        ScheduleDecision.Reason.BeforeWindow => $"等到 {config.Schedule.StartTime:HH:mm} 开拍",
        ScheduleDecision.Reason.AfterWindow => "今天已收工",
        ScheduleDecision.Reason.TooSoon => "运行中",
        ScheduleDecision.Reason.Paused => $"已暂停至 {config.PausedUntil:HH:mm}",
        _ => "运行中",
    };

    private async Task OnCaptureRequestedAsync(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Storage.OutputFolder)) return;
        if (string.IsNullOrWhiteSpace(config.Camera.DeviceMoniker)) return;

        var result = await _cameraService.TryCaptureAsync(
            config.Camera.DeviceMoniker,
            config.Camera.Width,
            config.Camera.Height,
            config.Camera.JpegQuality);

        if (!result.Success)
        {
            _stickyBusyUntil = DateTime.Now.Add(StickyBusyDuration);
            return;
        }

        try
        {
            var now = DateTime.Now;
            var dayDir = Path.Combine(config.Storage.OutputFolder, now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayDir);
            var path = Path.Combine(dayDir, now.ToString("HH-mm-ss") + ".jpg");
            await File.WriteAllBytesAsync(path, result.JpegBytes!);
            _todayCount++;
        }
        catch
        {
            _stickyBusyUntil = DateTime.Now.Add(StickyBusyDuration);
        }
    }

    private async Task TriggerManualCaptureAsync()
    {
        var config = _configStore.Load();
        if (string.IsNullOrWhiteSpace(config.Camera.DeviceMoniker))
        {
            MessageBox.Show("摄像头未配置。请打开配置文件填入 Camera.DeviceMoniker。",
                "ClassLapse", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Storage.OutputFolder))
        {
            MessageBox.Show("输出文件夹未配置。请打开配置文件填入 Storage.OutputFolder。",
                "ClassLapse", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await OnCaptureRequestedAsync(config);
    }

    private void PauseFor(TimeSpan span)
    {
        var config = _configStore.Load();
        config.PausedUntil = DateTime.Now.Add(span);
        _configStore.Save(config);
    }

    private void PauseUntilEndOfDay()
    {
        var config = _configStore.Load();
        config.PausedUntil = DateTime.Today.AddDays(1).AddSeconds(-1);
        _configStore.Save(config);
    }

    private void Resume()
    {
        var config = _configStore.Load();
        config.PausedUntil = null;
        _configStore.Save(config);
    }

    private void OpenOutputFolder()
    {
        var folder = _configStore.Load().Storage.OutputFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("输出文件夹尚未配置或不存在。", "ClassLapse",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void OpenConfigFile()
    {
        var path = _configStore.FilePath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _configStore.Save(_configStore.Load());
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
