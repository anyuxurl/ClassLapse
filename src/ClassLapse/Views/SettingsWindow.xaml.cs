using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClassLapse.Core;
using ClassLapse.Models;
using Microsoft.Win32;

namespace ClassLapse.Views;

[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private static readonly string[] CircledNumbers =
        { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩" };

    private readonly ConfigStore _configStore;
    private readonly CameraService _cameraService;
    private readonly bool _isFirstRun;
    private AppConfig _config;
    private bool _loaded;

    private readonly List<TimeWindowRow> _windowRows = new();

    public SettingsWindow(ConfigStore configStore, CameraService cameraService, bool isFirstRun = false)
    {
        _configStore = configStore;
        _cameraService = cameraService;
        _isFirstRun = isFirstRun;
        _config = _configStore.Load();

        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstRun)
        {
            FirstRunBanner.Visibility = Visibility.Visible;
            CancelButton.Content = "退出程序";
            Title = "ClassLapse · 首次运行设置";
        }

        PopulateFromConfig(_config);
        PopulateCameras(selectMoniker: _config.Camera.DeviceMoniker);
        VersionText.Text = "版本 " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
        ConfigPathBox.Text = _configStore.FilePath;
        LogDirBox.Text = Log.Instance.LogDir;
        AutoStartCheck.IsChecked = AutoStartManager.IsEnabled();
        ApplyHighestResolutionLock();
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

        TimeWindowsPanel.Children.Clear();
        _windowRows.Clear();
        if (config.Schedule.TimeWindows.Length == 0)
        {
            AddTimeWindowRow(new TimeOnly(8, 0), new TimeOnly(17, 0));
        }
        else
        {
            foreach (var w in config.Schedule.TimeWindows)
            {
                AddTimeWindowRow(w.Start, w.End);
            }
        }

        IntervalBox.Text = config.Schedule.IntervalSeconds.ToString();

        HighestResCheck.IsChecked = config.Camera.UseHighestResolution;
        WidthBox.Text = config.Camera.Width.ToString();
        HeightBox.Text = config.Camera.Height.ToString();
        QualitySlider.Value = config.Camera.JpegQuality;
        QualityBox.Text = config.Camera.JpegQuality.ToString();

        OutputFolderBox.Text = config.Storage.OutputFolder;
        AutoCleanupCheck.IsChecked = config.Storage.AutoCleanupEnabled;
        CleanupDaysBox.Text = config.Storage.AutoCleanupDays.ToString();
        MaxDiskBox.Text = config.Storage.MaxDiskUsageGB.ToString();
        OnAutoCleanupToggle(this, new RoutedEventArgs());

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

    // ----- time windows dynamic rows -----

    private sealed class TimeWindowRow
    {
        public Border Container { get; }
        public TextBox StartBox { get; }
        public TextBox EndBox { get; }
        public TextBlock IndexText { get; }

        public TimeWindowRow(Border container, TextBox start, TextBox end, TextBlock indexText)
        {
            Container = container;
            StartBox = start;
            EndBox = end;
            IndexText = indexText;
        }
    }

    private void AddTimeWindowRow(TimeOnly start, TimeOnly end)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var indexText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.DimGray,
            FontSize = 14,
        };
        Grid.SetColumn(indexText, 0);
        grid.Children.Add(indexText);

        var startBox = new TextBox
        {
            Text = start.ToString("HH:mm"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
        };
        startBox.TextChanged += OnScheduleChanged;
        Grid.SetColumn(startBox, 1);
        grid.Children.Add(startBox);

        var toLabel = new TextBlock
        {
            Text = "至",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
        };
        Grid.SetColumn(toLabel, 2);
        grid.Children.Add(toLabel);

        var endBox = new TextBox
        {
            Text = end.ToString("HH:mm"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
        };
        endBox.TextChanged += OnScheduleChanged;
        Grid.SetColumn(endBox, 3);
        grid.Children.Add(endBox);

        var deleteBtn = new Button
        {
            Content = "删除",
            Padding = new Thickness(4, 1, 4, 1),
        };
        Grid.SetColumn(deleteBtn, 5);
        grid.Children.Add(deleteBtn);

        var border = new Border { Child = grid };
        var row = new TimeWindowRow(border, startBox, endBox, indexText);
        deleteBtn.Click += (_, _) =>
        {
            _windowRows.Remove(row);
            TimeWindowsPanel.Children.Remove(border);
            RefreshWindowRowIndices();
            UpdateEstimate();
        };

        _windowRows.Add(row);
        TimeWindowsPanel.Children.Add(border);
        RefreshWindowRowIndices();
    }

    private void RefreshWindowRowIndices()
    {
        for (int i = 0; i < _windowRows.Count; i++)
        {
            _windowRows[i].IndexText.Text = i < CircledNumbers.Length
                ? CircledNumbers[i]
                : (i + 1).ToString();
        }
    }

    private void OnAddTimeWindowClick(object sender, RoutedEventArgs e)
    {
        TimeOnly defaultStart = new(8, 0);
        TimeOnly defaultEnd = new(11, 30);
        if (_windowRows.Count > 0)
        {
            // Default to "right after the last window ends"
            if (TimeOnly.TryParseExact(_windowRows[^1].EndBox.Text, "HH:mm", out var prevEnd))
            {
                defaultStart = prevEnd;
                defaultEnd = TimeOnly.FromTimeSpan(prevEnd.ToTimeSpan().Add(TimeSpan.FromHours(2)));
                if (defaultEnd < defaultStart) defaultEnd = new TimeOnly(23, 59);
            }
        }
        AddTimeWindowRow(defaultStart, defaultEnd);
        UpdateEstimate();
    }

    // ----- estimate -----

    private void OnScheduleChanged(object sender, TextChangedEventArgs e) => UpdateEstimate();
    private void OnScheduleStructureChanged(object sender, RoutedEventArgs e) => UpdateEstimate();

    private void UpdateEstimate()
    {
        if (!_loaded) return;

        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 1)
        {
            EstimateText.Text = "（请填写有效的拍照间隔）";
            return;
        }

        if (!TryCollectTimeWindows(out var windows, out string? winErr))
        {
            EstimateText.Text = "（时段配置错误：" + winErr + "）";
            return;
        }

        int dayCount = CollectActiveDays().Length;
        if (dayCount == 0)
        {
            EstimateText.Text = "（至少要勾选一个生效日）";
            return;
        }

        double totalSecondsPerDay = 0;
        var windowParts = new List<string>();
        foreach (var w in windows)
        {
            double seconds = (w.End.ToTimeSpan() - w.Start.ToTimeSpan()).TotalSeconds;
            totalSecondsPerDay += seconds;
            windowParts.Add($"{w.Start:HH:mm}-{w.End:HH:mm} ({seconds / 60:N0} 分)");
        }

        int perDay = (int)Math.Floor(totalSecondsPerDay / interval) + windows.Length;
        int perWeek = perDay * dayCount;
        var approxMb = perDay * 800.0 / 1024.0; // assume ~800KB/jpg at full res

        EstimateText.Text =
            $"每个生效日 {windows.Length} 段（{string.Join("，", windowParts)}）共 {totalSecondsPerDay / 60:N0} 分钟" +
            $"\n约 {perDay:N0} 张/天，{perWeek:N0} 张/周（按全分辨率 ~800KB 估算，约 {approxMb:N1} MB/天）";
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

    private bool TryCollectTimeWindows(out TimeWindow[] windows, out string? error)
    {
        windows = Array.Empty<TimeWindow>();
        error = null;

        if (_windowRows.Count == 0)
        {
            error = "至少要保留一个时段";
            return false;
        }

        var list = new List<TimeWindow>(_windowRows.Count);
        for (int i = 0; i < _windowRows.Count; i++)
        {
            var row = _windowRows[i];
            string startText = row.StartBox.Text.Trim();
            string endText = row.EndBox.Text.Trim();
            if (!TimeOnly.TryParseExact(startText, "HH:mm", out var s))
            {
                error = $"时段 {i + 1} 的起始时间 '{startText}' 格式应为 HH:mm";
                return false;
            }
            if (!TimeOnly.TryParseExact(endText, "HH:mm", out var endVal))
            {
                error = $"时段 {i + 1} 的结束时间 '{endText}' 格式应为 HH:mm";
                return false;
            }
            if (endVal <= s)
            {
                error = $"时段 {i + 1} 的结束时间必须晚于起始时间";
                return false;
            }
            list.Add(new TimeWindow(s, endVal));
        }

        // sort + 简单重叠检测
        var sorted = list.OrderBy(w => w.Start).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Start < sorted[i - 1].End)
            {
                error = $"时段 {sorted[i - 1].Start:HH:mm}-{sorted[i - 1].End:HH:mm} 与 {sorted[i].Start:HH:mm}-{sorted[i].End:HH:mm} 重叠";
                return false;
            }
        }

        windows = sorted.ToArray();
        return true;
    }

    // ----- camera / quality / cleanup toggles -----

    private void OnHighestResToggle(object sender, RoutedEventArgs e)
    {
        ApplyHighestResolutionLock();
    }

    private void ApplyHighestResolutionLock()
    {
        bool useHighest = HighestResCheck.IsChecked == true;
        WidthBox.IsEnabled = !useHighest;
        HeightBox.IsEnabled = !useHighest;
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
        bool useHighest = HighestResCheck.IsChecked == true;

        CameraTestResult.Text = "正在拍摄...";
        var result = await _cameraService.TryCaptureAsync(
            device.MonikerString, w, h, q, useHighestResolution: useHighest);

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

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        var dir = Log.Instance.LogDir;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("无法打开日志目录: " + ex.Message);
        }
    }

    // ----- save / cancel -----

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

        ApplyAutoStart(built.AutoStartWithWindows);

        DialogResult = true;
        Close();
    }

    private static void ApplyAutoStart(bool wanted)
    {
        try
        {
            bool current = AutoStartManager.IsEnabled();
            if (wanted && !current)
            {
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                AutoStartManager.Enable(exePath);
                Log.Info($"auto-start enabled -> {exePath}");
            }
            else if (!wanted && current)
            {
                AutoStartManager.Disable();
                Log.Info("auto-start disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Error("failed to update auto-start registry", ex);
        }
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

        if (!TryCollectTimeWindows(out var windows, out string? winError))
        {
            error = winError;
            return false;
        }

        if (!int.TryParse(IntervalBox.Text, out int interval) || interval < 1)
        {
            error = "拍照间隔必须是大于等于 1 的整数";
            return false;
        }

        bool useHighest = HighestResCheck.IsChecked == true;
        int width = _config.Camera.Width;
        int height = _config.Camera.Height;
        if (!useHighest)
        {
            if (!int.TryParse(WidthBox.Text, out width) || width < 1)
            {
                error = "宽度必须是正整数";
                return false;
            }
            if (!int.TryParse(HeightBox.Text, out height) || height < 1)
            {
                error = "高度必须是正整数";
                return false;
            }
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
        if (selectedCam == null)
        {
            error = "请选择一个摄像头";
            return false;
        }

        built = new AppConfig
        {
            Schedule = new ScheduleConfig
            {
                ActiveDays = days,
                TimeWindows = windows,
                IntervalSeconds = interval,
            },
            Camera = new CameraConfig
            {
                DeviceMoniker = selectedCam.MonikerString,
                FriendlyName = selectedCam.FriendlyName,
                UseHighestResolution = useHighest,
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
