using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClassLapse.Core;
using ClassLapse.Models;
using ClassLapse.Views;
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
    private readonly StorageJanitor _janitor;
    private readonly CaptureFileStore _captureStore;
    private readonly CaptureJournal _captureJournal;
    private readonly SemaphoreSlim _captureGate = new(initialCount: 1, maxCount: 1);

    private DateTime _todayStartedAt = DateTime.Today;
    private int _todayCount;
    private DateTime? _lastSuccessAt;
    private IconState _currentState = IconState.Green;
    private DateTime? _stickyBusyUntil;

    private MenuItem? _statusItem;
    private MenuItem? _todayItem;
    private MenuItem? _lastCaptureItem;
    private MenuItem? _cameraItem;
    private SettingsWindow? _settingsWindow;
    private TimelapseWindow? _timelapseWindow;

    public TrayApp(ConfigStore configStore, CameraService cameraService, CaptureScheduler scheduler)
    {
        _configStore = configStore;
        _cameraService = cameraService;
        _scheduler = scheduler;
        _janitor = new StorageJanitor();
        _captureStore = new CaptureFileStore();
        _captureJournal = new CaptureJournal();
        _lastSuccessAt = _captureJournal.FindLastSuccessfulCapture();

        try
        {
            var config = _configStore.Load();
            _todayCount = _captureStore.CountCapturesForDay(
                config.Storage.OutputFolder,
                DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            Log.Warn("failed to initialize capture counters: " + ex.Message);
        }

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

        _lastCaptureItem = new MenuItem { Header = "最后成功: 尚无记录", IsEnabled = false };
        menu.Items.Add(_lastCaptureItem);

        _cameraItem = new MenuItem { Header = "摄像头: 未配置", IsEnabled = false };
        menu.Items.Add(_cameraItem);

        menu.Items.Add(new Separator());

        var pause1h = new MenuItem { Header = "⏸  暂停 1 小时" };
        pause1h.Click += (_, _) => PauseFor(TimeSpan.FromHours(1));
        menu.Items.Add(pause1h);

        var pauseToday = new MenuItem { Header = "⏸  暂停今天剩余" };
        pauseToday.Click += (_, _) => PauseUntilEndOfDay();
        menu.Items.Add(pauseToday);

        var pauseIndef = new MenuItem { Header = "⏸  持续暂停（手动恢复）" };
        pauseIndef.Click += (_, _) => PauseIndefinitely();
        menu.Items.Add(pauseIndef);

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

        var timelapse = new MenuItem { Header = "🎬  合成延时视频..." };
        timelapse.Click += (_, _) => OpenTimelapse();
        menu.Items.Add(timelapse);

        var settings = new MenuItem { Header = "⚙️  设置..." };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var openConfig = new MenuItem { Header = "📝  打开配置文件 (高级)" };
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
                _todayCount = _captureStore.CountCapturesForDay(
                    config.Storage.OutputFolder,
                    DateOnly.FromDateTime(_todayStartedAt));
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
        if (_lastCaptureItem != null)
        {
            _lastCaptureItem.Header = FormatLastSuccess(_lastSuccessAt);
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
        ScheduleDecision.Reason.OutsideTimeWindows => DescribeNextWindow(config),
        ScheduleDecision.Reason.TooSoon => "运行中",
        ScheduleDecision.Reason.Paused => config.PausedIndefinitely
            ? "已暂停（需手动恢复）"
            : $"已暂停至 {config.PausedUntil:HH:mm}",
        _ => "运行中",
    };

    private static string DescribeNextWindow(AppConfig config)
    {
        var now = DateTime.Now;
        var nowTime = TimeOnly.FromDateTime(now);
        var today = now.DayOfWeek;

        // Earliest still-upcoming capture time today across every enabled entry active today:
        // interval entries contribute their window start, specific entries each remaining point.
        TimeOnly? next = null;
        foreach (var entry in config.Schedule.Entries)
        {
            if (!entry.Enabled) continue;
            if (Array.IndexOf(entry.ActiveDays, today) < 0) continue;

            if (entry.Mode == ScheduleMode.Interval)
            {
                Consider(entry.Window.Start);
            }
            else
            {
                foreach (var p in entry.Times) Consider(p);
            }
        }

        return next.HasValue ? $"等到 {next.Value:HH:mm} 开拍" : "今天已收工";

        void Consider(TimeOnly t)
        {
            if (t > nowTime && (next is null || t < next)) next = t;
        }
    }

    private async Task OnCaptureRequestedAsync(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Storage.OutputFolder)) return;
        if (string.IsNullOrWhiteSpace(config.Camera.DeviceMoniker)) return;

        await _captureGate.WaitAsync().ConfigureAwait(false);
        var captureTime = DateTime.Now;
        try
        {
            // One timestamp drives both the burned-in watermark and the on-disk filename.
            var watermark = config.Watermark;
            Action<System.Drawing.Bitmap>? beforeEncode = watermark.Enabled
                ? frame => ApplyWatermark(frame, captureTime, watermark)
                : null;

            var result = await _cameraService.TryCaptureAsync(
                config.Camera.DeviceMoniker,
                config.Camera.Width,
                config.Camera.Height,
                config.Camera.JpegQuality,
                useHighestResolution: config.Camera.UseHighestResolution,
                beforeEncode: beforeEncode).ConfigureAwait(false);

            if (!result.Success)
            {
                _stickyBusyUntil = DateTime.Now.Add(StickyBusyDuration);
                Log.Warn($"capture failed: {result.Failure} after {result.ElapsedMilliseconds}ms — {result.ErrorMessage}");
                RecordCapture(new CaptureJournal.Entry(
                    captureTime, false, null, 0, 0, 0, result.ElapsedMilliseconds,
                    result.Failure.ToString(), result.ErrorMessage));
                return;
            }

            try
            {
                string path = await _captureStore.SaveAsync(
                    config.Storage.OutputFolder,
                    captureTime,
                    result.JpegBytes!).ConfigureAwait(false);
                _todayCount++;
                _lastSuccessAt = captureTime;
                RecordCapture(new CaptureJournal.Entry(
                    captureTime, true, path, result.Width, result.Height, result.JpegBytes!.Length,
                    result.ElapsedMilliseconds, null, null));
                Log.Info($"captured {result.Width}x{result.Height} {result.JpegBytes.Length / 1024.0:N1}KB in {result.ElapsedMilliseconds}ms -> {Path.GetFileName(path)}");

                _janitor.RunIfDue(config.Storage);
            }
            catch (Exception ex)
            {
                _stickyBusyUntil = DateTime.Now.Add(StickyBusyDuration);
                RecordCapture(new CaptureJournal.Entry(
                    captureTime, false, null, result.Width, result.Height, result.JpegBytes!.Length,
                    result.ElapsedMilliseconds, "StorageWrite", ex.Message));
                Log.Error("failed to write capture to disk", ex);
            }
        }
        catch (Exception ex)
        {
            _stickyBusyUntil = DateTime.Now.Add(StickyBusyDuration);
            RecordCapture(new CaptureJournal.Entry(
                captureTime, false, null, 0, 0, 0, 0, "Unexpected", ex.Message));
            Log.Error("unexpected capture failure", ex);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void RecordCapture(CaptureJournal.Entry entry)
    {
        _captureJournal.TryAppend(entry);
    }

    private static string FormatLastSuccess(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "最后成功: 尚无记录";
        return timestamp.Value.Date == DateTime.Today
            ? $"最后成功: 今天 {timestamp.Value:HH:mm:ss}"
            : $"最后成功: {timestamp.Value:yyyy-MM-dd HH:mm:ss}";
    }

    private static void ApplyWatermark(System.Drawing.Bitmap frame, DateTime timestamp, WatermarkConfig cfg)
    {
        try
        {
            TimestampWatermark.Draw(frame, timestamp, cfg);
        }
        catch (Exception ex)
        {
            // A watermark failure must never cost us the photo — save the frame without it.
            Log.Warn("watermark draw failed; saving frame without it: " + ex.Message);
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

    // Each pause action writes a full, mutually-exclusive state (the last click wins), so switching
    // between timed and open-ended pause never leaves a stale flag behind.
    private void PauseFor(TimeSpan span)
    {
        var config = _configStore.Load();
        config.PausedIndefinitely = false;
        config.PausedUntil = DateTime.Now.Add(span);
        _configStore.Save(config);
    }

    private void PauseUntilEndOfDay()
    {
        var config = _configStore.Load();
        config.PausedIndefinitely = false;
        config.PausedUntil = DateTime.Today.AddDays(1).AddSeconds(-1);
        _configStore.Save(config);
    }

    // Open-ended "vacation" pause: stays paused across restarts until 恢复 is clicked.
    private void PauseIndefinitely()
    {
        var config = _configStore.Load();
        config.PausedIndefinitely = true;
        config.PausedUntil = null;
        _configStore.Save(config);
    }

    private void Resume()
    {
        var config = _configStore.Load();
        config.PausedIndefinitely = false;
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

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_configStore, _cameraService);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OpenTimelapse()
    {
        if (_timelapseWindow != null)
        {
            _timelapseWindow.Activate();
            return;
        }

        var folder = _configStore.Load().Storage.OutputFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("输出文件夹尚未配置或不存在，暂无照片可合成。", "ClassLapse",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _timelapseWindow = new TimelapseWindow(_configStore);
        _timelapseWindow.Closed += (_, _) => _timelapseWindow = null;
        _timelapseWindow.Show();
    }

}
