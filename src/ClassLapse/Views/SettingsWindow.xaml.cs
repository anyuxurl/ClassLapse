using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClassLapse.Core;
using ClassLapse.Models;
using Microsoft.Win32;

namespace ClassLapse.Views;

[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private static readonly string[] CircledNumbers =
        { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩" };

    private static readonly string[] DayNames =
        { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private static readonly DayOfWeek[] DayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    // Watermark position dropdown, in display order; index maps 1:1 to PositionNames.
    private static readonly WatermarkPosition[] PositionOrder =
    {
        WatermarkPosition.BottomRight, WatermarkPosition.TopRight,
        WatermarkPosition.BottomLeft, WatermarkPosition.TopLeft,
    };

    private static readonly string[] PositionNames = { "右下", "右上", "左下", "左上" };

    private readonly ConfigStore _configStore;
    private readonly CameraService _cameraService;
    private readonly bool _isFirstRun;
    private AppConfig _config;
    private bool _loaded;

    private readonly List<EntryCard> _entryCards = new();

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

        foreach (var name in PositionNames) WatermarkPositionCombo.Items.Add(name);

        PopulateFromConfig(_config);
        PopulateCameras(selectMoniker: _config.Camera.DeviceMoniker);
        VersionText.Text = "版本 " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知");
        ConfigPathBox.Text = _configStore.FilePath;
        LogDirBox.Text = Log.Instance.LogDir;
        AutoStartCheck.IsChecked = AutoStartManager.IsEnabled();
        ApplyHighestResolutionLock();
        _loaded = true;
        UpdateEstimate();
        RefreshWatermarkPreview();
    }

    private void PopulateFromConfig(AppConfig config)
    {
        EntriesPanel.Children.Clear();
        _entryCards.Clear();
        if (config.Schedule.Entries.Length == 0)
        {
            AddEntryCard(NewDefaultEntry());
        }
        else
        {
            foreach (var entry in config.Schedule.Entries) AddEntryCard(entry);
        }

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

        var wm = config.Watermark;
        WatermarkEnableCheck.IsChecked = wm.Enabled;
        int posIdx = Array.IndexOf(PositionOrder, wm.Position);
        WatermarkPositionCombo.SelectedIndex = posIdx >= 0 ? posIdx : 0;
        WatermarkFormatBox.Text = wm.Format;
        WatermarkFontSizeBox.Text = wm.FontSize.ToString();
        WatermarkColorBox.Text = wm.Color;
        WatermarkOutlineCheck.IsChecked = wm.Outline;
        ApplyWatermarkEnabledLock();

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

    // ----- schedule entry cards -----

    private sealed class EntryCard
    {
        public string Id = "";
        public Border Container = null!;
        public TextBlock IndexText = null!;
        public CheckBox EnabledCheck = null!;
        public TextBox NameBox = null!;
        public ComboBox ModeCombo = null!;
        public CheckBox[] DayChecks = null!; // index 0=Mon .. 6=Sun, aligned with DayOrder
        public Panel IntervalPanel = null!;
        public TextBox StartBox = null!;
        public TextBox EndBox = null!;
        public TextBox IntervalBox = null!;
        public Panel SpecificPanel = null!;
        public TextBox TimesBox = null!;

        public bool IsSpecific => ModeCombo.SelectedIndex == 1;
    }

    private static ScheduleEntry NewDefaultEntry() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Mode = ScheduleMode.Interval,
        Window = new TimeWindow(new TimeOnly(8, 0), new TimeOnly(11, 30)),
        IntervalSeconds = 30,
    };

    private void AddEntryCard(ScheduleEntry entry)
    {
        var card = new EntryCard
        {
            Id = string.IsNullOrEmpty(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
        };

        var outer = new StackPanel();

        // --- header: index | 启用 | name | mode | delete ---
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        card.IndexText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
            FontSize = 14,
        };
        Grid.SetColumn(card.IndexText, 0);
        header.Children.Add(card.IndexText);

        card.EnabledCheck = new CheckBox
        {
            Content = "启用",
            IsChecked = entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        card.EnabledCheck.Click += OnScheduleStructureChanged;
        Grid.SetColumn(card.EnabledCheck, 1);
        header.Children.Add(card.EnabledCheck);

        card.NameBox = new TextBox
        {
            Text = entry.Name,
            ToolTip = "条目名称（可选，如 上午 / 打铃）",
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
            MinWidth = 120,
        };
        Grid.SetColumn(card.NameBox, 2);
        header.Children.Add(card.NameBox);

        card.ModeCombo = new ComboBox
        {
            Width = 88,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        card.ModeCombo.Items.Add("间隔");
        card.ModeCombo.Items.Add("定时");
        card.ModeCombo.SelectedIndex = entry.Mode == ScheduleMode.SpecificTimes ? 1 : 0;
        Grid.SetColumn(card.ModeCombo, 3);
        header.Children.Add(card.ModeCombo);

        var deleteBtn = new Button
        {
            Content = "删除",
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(8, 0, 0, 0),
        };
        deleteBtn.Style = (Style)FindResource("DangerButton");
        Grid.SetColumn(deleteBtn, 4);
        header.Children.Add(deleteBtn);

        outer.Children.Add(header);

        // --- days ---
        var days = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        card.DayChecks = new CheckBox[7];
        for (int i = 0; i < 7; i++)
        {
            var c = new CheckBox
            {
                Content = DayNames[i],
                IsChecked = entry.ActiveDays.Contains(DayOrder[i]),
                Margin = new Thickness(0, 0, 10, 0),
            };
            c.Click += OnScheduleStructureChanged;
            card.DayChecks[i] = c;
            days.Children.Add(c);
        }
        outer.Children.Add(days);

        // --- interval sub-panel ---
        var ip = new StackPanel { Orientation = Orientation.Horizontal };
        ip.Children.Add(new TextBlock { Text = "时段 ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray });
        card.StartBox = MakeTimeBox(entry.Window.Start);
        ip.Children.Add(card.StartBox);
        ip.Children.Add(new TextBlock { Text = " 至 ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray });
        card.EndBox = MakeTimeBox(entry.Window.End);
        ip.Children.Add(card.EndBox);
        ip.Children.Add(new TextBlock { Text = "     每 ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray });
        card.IntervalBox = new TextBox
        {
            Text = entry.IntervalSeconds.ToString(),
            Width = 56,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
        };
        card.IntervalBox.TextChanged += OnScheduleChanged;
        ip.Children.Add(card.IntervalBox);
        ip.Children.Add(new TextBlock { Text = " 秒/张", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray });
        card.IntervalPanel = ip;
        outer.Children.Add(ip);

        // --- specific-times sub-panel ---
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = "时间点 ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray });
        card.TimesBox = new TextBox
        {
            Text = string.Join(", ", entry.Times.Select(t => t.ToString("HH:mm"))),
            Width = 300,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
        };
        card.TimesBox.TextChanged += OnScheduleChanged;
        sp.Children.Add(card.TimesBox);
        sp.Children.Add(new TextBlock { Text = "  HH:mm，逗号分隔", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });
        card.SpecificPanel = sp;
        outer.Children.Add(sp);

        card.Container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 6, 0, 0),
            Child = outer,
        };

        deleteBtn.Click += (_, _) =>
        {
            _entryCards.Remove(card);
            EntriesPanel.Children.Remove(card.Container);
            RefreshEntryCardIndices();
            UpdateEstimate();
        };

        card.ModeCombo.SelectionChanged += (_, _) =>
        {
            ApplyCardMode(card);
            // A mode flip changes what the per-entry last-capture means, so mint a fresh id to
            // stop the scheduler's stale timing for the old id from mis-deduping the new mode.
            card.Id = Guid.NewGuid().ToString("N");
            UpdateEstimate();
        };

        ApplyCardMode(card); // initial sub-panel visibility (handler not yet wired during construction)

        _entryCards.Add(card);
        EntriesPanel.Children.Add(card.Container);
        RefreshEntryCardIndices();
    }

    private TextBox MakeTimeBox(TimeOnly t)
    {
        var box = new TextBox
        {
            Text = t.ToString("HH:mm"),
            Width = 64,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
        };
        box.TextChanged += OnScheduleChanged;
        return box;
    }

    private static void ApplyCardMode(EntryCard card)
    {
        bool specific = card.IsSpecific;
        card.IntervalPanel.Visibility = specific ? Visibility.Collapsed : Visibility.Visible;
        card.SpecificPanel.Visibility = specific ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshEntryCardIndices()
    {
        for (int i = 0; i < _entryCards.Count; i++)
        {
            _entryCards[i].IndexText.Text = i < CircledNumbers.Length
                ? CircledNumbers[i]
                : (i + 1).ToString();
        }
    }

    private void OnAddEntryClick(object sender, RoutedEventArgs e)
    {
        AddEntryCard(NewDefaultEntry());
        UpdateEstimate();
    }

    // ----- estimate -----

    private void OnScheduleChanged(object sender, TextChangedEventArgs e) => UpdateEstimate();
    private void OnScheduleStructureChanged(object sender, RoutedEventArgs e) => UpdateEstimate();

    private void UpdateEstimate()
    {
        if (!_loaded) return;

        if (_entryCards.Count == 0)
        {
            EstimateText.Text = "（至少添加一个条目）";
            return;
        }

        int totalPerWeek = 0;
        var lines = new List<string>();
        foreach (var card in _entryCards)
        {
            if (card.EnabledCheck.IsChecked != true) continue;

            int dayCount = CountDays(card);
            if (dayCount == 0)
            {
                lines.Add($"{CardLabel(card)}：未勾选生效日");
                continue;
            }

            if (!card.IsSpecific)
            {
                if (!TimeOnly.TryParseExact(card.StartBox.Text.Trim(), "HH:mm", out var s) ||
                    !TimeOnly.TryParseExact(card.EndBox.Text.Trim(), "HH:mm", out var en) ||
                    en <= s ||
                    !int.TryParse(card.IntervalBox.Text, out var interval) || interval < 1)
                {
                    lines.Add($"{CardLabel(card)}：时段/间隔无效");
                    continue;
                }
                double seconds = (en.ToTimeSpan() - s.ToTimeSpan()).TotalSeconds;
                int perDay = (int)Math.Floor(seconds / interval) + 1;
                totalPerWeek += perDay * dayCount;
                lines.Add($"{CardLabel(card)}：{s:HH:mm}-{en:HH:mm} 每 {interval}s ≈ {perDay} 张/天 × {dayCount} 天");
            }
            else
            {
                if (!TryParseTimes(card.TimesBox.Text, out var times) || times.Length == 0)
                {
                    lines.Add($"{CardLabel(card)}：时间点无效");
                    continue;
                }
                totalPerWeek += times.Length * dayCount;
                lines.Add($"{CardLabel(card)}：{times.Length} 个时间点 × {dayCount} 天");
            }
        }

        if (lines.Count == 0)
        {
            EstimateText.Text = "（没有启用的条目）";
            return;
        }

        double mbPerWeek = totalPerWeek * 800.0 / 1024.0; // assume ~800KB/jpg at full res
        EstimateText.Text = string.Join("\n", lines) +
            $"\n合计约 {totalPerWeek:N0} 张/周（全分辨率 ~800KB 估算，约 {mbPerWeek:N0} MB/周）";
    }

    private static string CardLabel(EntryCard card)
    {
        var name = card.NameBox.Text.Trim();
        return string.IsNullOrEmpty(name) ? (card.IsSpecific ? "定时条目" : "间隔条目") : name;
    }

    private static int CountDays(EntryCard card)
    {
        int n = 0;
        foreach (var c in card.DayChecks)
        {
            if (c.IsChecked == true) n++;
        }
        return n;
    }

    private static DayOfWeek[] CollectDays(EntryCard card)
    {
        var list = new List<DayOfWeek>(7);
        for (int i = 0; i < 7; i++)
        {
            if (card.DayChecks[i].IsChecked == true) list.Add(DayOrder[i]);
        }
        return list.ToArray();
    }

    private static bool TryParseTimes(string text, out TimeOnly[] times)
    {
        times = Array.Empty<TimeOnly>();
        var parts = text.Split(
            new[] { ',', '，', ' ', '\t', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var list = new List<TimeOnly>(parts.Length);
        foreach (var p in parts)
        {
            if (!TimeOnly.TryParseExact(p, "HH:mm", out var t)) return false;
            list.Add(t);
        }
        times = list.ToArray();
        return true;
    }

    private bool TryCollectEntries(out ScheduleEntry[] entries, out string? error)
    {
        entries = Array.Empty<ScheduleEntry>();
        error = null;

        if (_entryCards.Count == 0)
        {
            error = "至少要添加一个拍照条目";
            return false;
        }

        var list = new List<ScheduleEntry>(_entryCards.Count);
        for (int i = 0; i < _entryCards.Count; i++)
        {
            var card = _entryCards[i];
            bool enabled = card.EnabledCheck.IsChecked == true;
            var days = CollectDays(card);
            if (enabled && days.Length == 0)
            {
                error = $"条目 {i + 1} 已启用，但没有勾选任何生效日";
                return false;
            }

            var entry = new ScheduleEntry
            {
                Id = string.IsNullOrEmpty(card.Id) ? Guid.NewGuid().ToString("N") : card.Id,
                Enabled = enabled,
                Name = card.NameBox.Text.Trim(),
                ActiveDays = days,
            };

            if (!card.IsSpecific)
            {
                entry.Mode = ScheduleMode.Interval;
                if (!TimeOnly.TryParseExact(card.StartBox.Text.Trim(), "HH:mm", out var s))
                {
                    error = $"条目 {i + 1} 的开始时间格式应为 HH:mm";
                    return false;
                }
                if (!TimeOnly.TryParseExact(card.EndBox.Text.Trim(), "HH:mm", out var en))
                {
                    error = $"条目 {i + 1} 的结束时间格式应为 HH:mm";
                    return false;
                }
                if (en <= s)
                {
                    error = $"条目 {i + 1} 的结束时间必须晚于开始时间";
                    return false;
                }
                if (!int.TryParse(card.IntervalBox.Text, out var interval) || interval < 1)
                {
                    error = $"条目 {i + 1} 的拍照间隔必须是 ≥ 1 的整数";
                    return false;
                }
                entry.Window = new TimeWindow(s, en);
                entry.IntervalSeconds = interval;
            }
            else
            {
                entry.Mode = ScheduleMode.SpecificTimes;
                if (!TryParseTimes(card.TimesBox.Text, out var times))
                {
                    error = $"条目 {i + 1} 的时间点格式应为 HH:mm（逗号分隔）";
                    return false;
                }
                if (times.Length == 0)
                {
                    error = $"条目 {i + 1} 至少要有一个时间点";
                    return false;
                }
                entry.Times = times.Distinct().OrderBy(t => t).ToArray();
            }

            list.Add(entry);
        }

        entries = list.ToArray();
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

    // ----- watermark -----

    private void OnWatermarkOptionChanged(object sender, RoutedEventArgs e)
    {
        ApplyWatermarkEnabledLock();
        RefreshWatermarkPreview();
    }

    private void OnWatermarkOptionChanged(object sender, SelectionChangedEventArgs e) => RefreshWatermarkPreview();
    private void OnWatermarkOptionChanged(object sender, TextChangedEventArgs e) => RefreshWatermarkPreview();

    private void ApplyWatermarkEnabledLock()
    {
        bool on = WatermarkEnableCheck.IsChecked == true;
        WatermarkPositionCombo.IsEnabled = on;
        WatermarkFormatBox.IsEnabled = on;
        WatermarkFontSizeBox.IsEnabled = on;
        WatermarkColorBox.IsEnabled = on;
        WatermarkOutlineCheck.IsEnabled = on;
    }

    private WatermarkConfig CollectWatermark()
    {
        int idx = WatermarkPositionCombo.SelectedIndex;
        var pos = (idx >= 0 && idx < PositionOrder.Length) ? PositionOrder[idx] : WatermarkPosition.BottomRight;

        int fontSize = 0;
        if (int.TryParse(WatermarkFontSizeBox.Text.Trim(), out int fs) && fs > 0) fontSize = fs;

        string format = string.IsNullOrWhiteSpace(WatermarkFormatBox.Text)
            ? "yyyy-MM-dd HH:mm:ss"
            : WatermarkFormatBox.Text; // keep spacing as typed
        string color = string.IsNullOrWhiteSpace(WatermarkColorBox.Text) ? "#FFFFFF" : WatermarkColorBox.Text.Trim();

        return new WatermarkConfig
        {
            Enabled = WatermarkEnableCheck.IsChecked == true,
            Position = pos,
            Format = format,
            FontSize = fontSize,
            Color = color,
            Outline = WatermarkOutlineCheck.IsChecked == true,
        };
    }

    private void RefreshWatermarkPreview()
    {
        if (!_loaded) return;
        try
        {
            var cfg = CollectWatermark();
            using var bmp = BuildPreviewBitmap(cfg, DateTime.Now);
            WatermarkPreview.Source = ToBitmapSource(bmp);
        }
        catch
        {
            // Preview is best-effort; a bad format/colour just falls back inside the renderer.
        }
    }

    private static System.Drawing.Bitmap BuildPreviewBitmap(WatermarkConfig cfg, DateTime ts)
    {
        // Render at a representative capture size so auto font-sizing matches what photos will look like;
        // the Image control downscales it. A dark→light gradient shows legibility on both extremes.
        const int w = 1280, h = 720;
        var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new System.Drawing.Rectangle(0, 0, w, h),
                   System.Drawing.Color.FromArgb(70, 80, 95),
                   System.Drawing.Color.FromArgb(205, 210, 215),
                   System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, 0, 0, w, h);
        }
        if (cfg.Enabled) TimestampWatermark.Draw(bmp, ts, cfg);
        return bmp;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
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

        TrySetDialogResult(true);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        TrySetDialogResult(false);
        Close();
    }

    /// <summary>
    /// Setting <see cref="Window.DialogResult"/> throws
    /// <see cref="InvalidOperationException"/> on a non-modal window
    /// (i.e. one opened via <c>Show()</c> rather than <c>ShowDialog()</c>).
    /// Swallow that case so closing the regular settings window from the
    /// tray menu does not propagate an unhandled UI-thread exception that
    /// would tear the whole application down.
    /// </summary>
    private void TrySetDialogResult(bool value)
    {
        try { DialogResult = value; }
        catch (InvalidOperationException) { /* opened via Show(), not ShowDialog() */ }
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

    private bool TryBuildConfig(out AppConfig built, out string? error)
    {
        built = new AppConfig();
        error = null;

        if (!TryCollectEntries(out var entries, out string? scheduleError))
        {
            error = scheduleError;
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
            SchemaVersion = _config.SchemaVersion,
            Schedule = new ScheduleConfig
            {
                Entries = entries,
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
            // Carry through sections this window doesn't edit so saving here never resets them.
            Timelapse = _config.Timelapse,
            Watermark = CollectWatermark(),
            AutoStartWithWindows = AutoStartCheck.IsChecked == true,
            PausedUntil = _config.PausedUntil,
            PausedIndefinitely = _config.PausedIndefinitely,
        };
        return true;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }
}
