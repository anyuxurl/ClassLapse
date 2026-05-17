using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using ClassLapse.Core;
using ClassLapse.Models;
using Microsoft.Win32;

namespace ClassLapse.Views;

[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private readonly ConfigStore _configStore;
    private readonly CameraService _cameraService;
    private AppConfig _config;
    private bool _loaded;

    public SettingsWindow(ConfigStore configStore, CameraService cameraService)
    {
        _configStore = configStore;
        _cameraService = cameraService;
        _config = _configStore.Load();

        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        PopulateFromConfig(_config);
        PopulateCameras(selectMoniker: _config.Camera.DeviceMoniker);
        VersionText.Text = "版本 " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
        ConfigPathBox.Text = _configStore.FilePath;
        _loaded = true;
        UpdateEstimate();
    }

    private void PopulateFromConfig(AppConfig config)
    {
        DayMon.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Monday);
        DayTue.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Tuesday);
        DayWed.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Wednesday);
        DayThu.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Thursday);
        DayFri.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Friday);
        DaySat.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Saturday);
        DaySun.IsChecked = config.Schedule.ActiveDays.Contains(DayOfWeek.Sunday);

        StartTimeBox.Text = config.Schedule.StartTime.ToString("HH:mm");
        EndTimeBox.Text = config.Schedule.EndTime.ToString("HH:mm");
        IntervalBox.Text = config.Schedule.IntervalSeconds.ToString();

        WidthBox.Text = config.Camera.Width.ToString();
        HeightBox.Text = config.Camera.Height.ToString();
        QualitySlider.Value = config.Camera.JpegQuality;
        QualityBox.Text = config.Camera.JpegQuality.ToString();

        OutputFolderBox.Text = config.Storage.OutputFolder;
        AutoCleanupCheck.IsChecked = config.Storage.AutoCleanupEnabled;
        CleanupDaysBox.Text = config.Storage.AutoCleanupDays.ToString();
        MaxDiskBox.Text = config.Storage.MaxDiskUsageGB.ToString();

        AutoStartCheck.IsChecked = config.AutoStartWithWindows;
    }

    private void PopulateCameras(string? selectMoniker)
    {
        try
        {
            var cameras = CameraEnumerator.Enumerate();
            CameraCombo.ItemsSource = cameras;

            if (!string.IsNullOrEmpty(selectMoniker))
            {
                var match = cameras.FirstOrDefault(c => c.MonikerString == selectMoniker);
                if (match != null)
                {
                    CameraCombo.SelectedItem = match;
                    return;
                }
            }

            if (cameras.Count > 0)
            {
                CameraCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"枚举摄像头失败: {ex.Message}");
        }
    }

    private void OnRefreshCamerasClick(object sender, RoutedEventArgs e)
    {
        var currentMoniker = (CameraCombo.SelectedItem as Models.CameraDevice)?.MonikerString;
        PopulateCameras(currentMoniker);
    }

    private void OnScheduleChanged(object sender, TextChangedEventArgs e) => UpdateEstimate();

    private void UpdateEstimate()
    {
        if (!_loaded) return;

        if (!TimeOnly.TryParseExact(StartTimeBox.Text, "HH:mm", out var start) ||
            !TimeOnly.TryParseExact(EndTimeBox.Text, "HH:mm", out var end) ||
            !int.TryParse(IntervalBox.Text, out var interval) ||
            interval < 1 || end <= start)
        {
            EstimateText.Text = "（请填写有效的时间窗口和间隔）";
            return;
        }

        int dayCount = CollectActiveDays().Length;
        if (dayCount == 0)
        {
            EstimateText.Text = "（至少要勾选一天）";
            return;
        }

        var windowSeconds = (end.ToTimeSpan() - start.ToTimeSpan()).TotalSeconds;
        int perDay = (int)Math.Floor(windowSeconds / interval) + 1;
        int perWeek = perDay * dayCount;
        var approxKb = perDay * 250.0 / 1024.0;

        EstimateText.Text =
            $"每个生效日约 {perDay:N0} 张，每周共 {perWeek:N0} 张" +
            $"（按 720p 平均 250KB 估算，单日约 {approxKb:N1} MB）";
    }

    private DayOfWeek[] CollectActiveDays()
    {
        var list = new List<DayOfWeek>(7);
        if (DayMon.IsChecked == true) list.Add(DayOfWeek.Monday);
        if (DayTue.IsChecked == true) list.Add(DayOfWeek.Tuesday);
        if (DayWed.IsChecked == true) list.Add(DayOfWeek.Wednesday);
        if (DayThu.IsChecked == true) list.Add(DayOfWeek.Thursday);
        if (DayFri.IsChecked == true) list.Add(DayOfWeek.Friday);
        if (DaySat.IsChecked == true) list.Add(DayOfWeek.Saturday);
        if (DaySun.IsChecked == true) list.Add(DayOfWeek.Sunday);
        return list.ToArray();
    }

    private void OnQualityChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;
        int q = (int)Math.Round(e.NewValue);
        if (QualityBox.Text != q.ToString()) QualityBox.Text = q.ToString();
    }

    private void OnQualityBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        if (int.TryParse(QualityBox.Text, out int q) && q >= 1 && q <= 100)
        {
            if (Math.Abs(QualitySlider.Value - q) > 0.5) QualitySlider.Value = q;
        }
    }

    private void OnAutoCleanupToggle(object sender, RoutedEventArgs e)
    {
        bool enabled = AutoCleanupCheck.IsChecked == true;
        CleanupDaysBox.IsEnabled = enabled;
        MaxDiskBox.IsEnabled = enabled;
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择照片输出文件夹",
            InitialDirectory = Directory.Exists(OutputFolderBox.Text)
                ? OutputFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderBox.Text = dialog.FolderName;
        }
    }

    private async void OnTestCaptureClick(object sender, RoutedEventArgs e)
    {
        if (CameraCombo.SelectedItem is not Models.CameraDevice device)
        {
            CameraTestResult.Text = "请先选择一个摄像头";
            return;
        }
        if (!int.TryParse(WidthBox.Text, out int w) || w < 1) w = 1280;
        if (!int.TryParse(HeightBox.Text, out int h) || h < 1) h = 720;
        if (!int.TryParse(QualityBox.Text, out int q) || q < 1 || q > 100) q = 85;

        CameraTestResult.Text = "正在拍摄...";
        var result = await _cameraService.TryCaptureAsync(device.MonikerString, w, h, q);

        if (!result.Success)
        {
            CameraTestResult.Text = $"失败 ({result.ElapsedMilliseconds}ms): {result.Failure} — {result.ErrorMessage}";
            return;
        }

        string tmpPath = Path.Combine(Path.GetTempPath(),
            $"classlapse-test-{DateTime.Now:HHmmss}.jpg");
        await File.WriteAllBytesAsync(tmpPath, result.JpegBytes!);
        CameraTestResult.Text =
            $"成功 — {result.Width}×{result.Height}, " +
            $"{result.JpegBytes!.Length / 1024.0:N1} KB, {result.ElapsedMilliseconds}ms。" +
            $" 已存至 {tmpPath}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = tmpPath, UseShellExecute = true });
        }
        catch { /* 不打开也无所谓 */ }
    }

    private void OnOpenConfigClick(object sender, RoutedEventArgs e)
    {
        var path = _configStore.FilePath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _configStore.Save(_configStore.Load());
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("无法打开配置文件: " + ex.Message);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildConfig(out var built, out string? validationError))
        {
            SetStatus("× " + validationError);
            return;
        }

        try
        {
            _configStore.Save(built);
        }
        catch (Exception ex)
        {
            SetStatus("× 写入失败: " + ex.Message);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool TryBuildConfig(out AppConfig built, out string? error)
    {
        built = new AppConfig();
        error = null;

        var days = CollectActiveDays();
        if (days.Length == 0)
        {
            error = "至少要勾选一个生效日";
            return false;
        }

        if (!TimeOnly.TryParseExact(StartTimeBox.Text, "HH:mm", out var start))
        {
            error = "起始时间格式错误，请用 HH:mm";
            return false;
        }
        if (!TimeOnly.TryParseExact(EndTimeBox.Text, "HH:mm", out var end))
        {
            error = "结束时间格式错误，请用 HH:mm";
            return false;
        }
        if (end <= start)
        {
            error = "结束时间必须晚于起始时间";
            return false;
        }

        if (!int.TryParse(IntervalBox.Text, out int interval) || interval < 1)
        {
            error = "拍照间隔必须是大于等于 1 的整数";
            return false;
        }

        if (!int.TryParse(WidthBox.Text, out int width) || width < 1)
        {
            error = "宽度必须是正整数";
            return false;
        }
        if (!int.TryParse(HeightBox.Text, out int height) || height < 1)
        {
            error = "高度必须是正整数";
            return false;
        }
        if (!int.TryParse(QualityBox.Text, out int quality) || quality < 1 || quality > 100)
        {
            error = "JPEG 质量必须在 1-100 之间";
            return false;
        }

        var outFolder = OutputFolderBox.Text.Trim();
        if (string.IsNullOrEmpty(outFolder))
        {
            error = "输出文件夹不能为空";
            return false;
        }
        try
        {
            Directory.CreateDirectory(outFolder);
        }
        catch (Exception ex)
        {
            error = "输出文件夹无法创建: " + ex.Message;
            return false;
        }

        int cleanupDays = _config.Storage.AutoCleanupDays;
        long maxGB = _config.Storage.MaxDiskUsageGB;
        bool autoCleanup = AutoCleanupCheck.IsChecked == true;
        if (autoCleanup)
        {
            if (!int.TryParse(CleanupDaysBox.Text, out cleanupDays) || cleanupDays < 1)
            {
                error = "保留天数必须是正整数";
                return false;
            }
            if (!long.TryParse(MaxDiskBox.Text, out maxGB) || maxGB < 0)
            {
                error = "磁盘上限必须是非负整数";
                return false;
            }
        }

        var selectedCam = CameraCombo.SelectedItem as Models.CameraDevice;

        built = new AppConfig
        {
            Schedule = new ScheduleConfig
            {
                ActiveDays = days,
                StartTime = start,
                EndTime = end,
                IntervalSeconds = interval,
            },
            Camera = new CameraConfig
            {
                DeviceMoniker = selectedCam?.MonikerString ?? _config.Camera.DeviceMoniker,
                FriendlyName = selectedCam?.FriendlyName ?? _config.Camera.FriendlyName,
                Width = width,
                Height = height,
                JpegQuality = quality,
            },
            Storage = new StorageConfig
            {
                OutputFolder = outFolder,
                AutoCleanupEnabled = autoCleanup,
                AutoCleanupDays = cleanupDays,
                MaxDiskUsageGB = maxGB,
            },
            AutoStartWithWindows = AutoStartCheck.IsChecked == true,
            PausedUntil = _config.PausedUntil,
        };
        return true;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }
}
