using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClassLapse.Core;
using ClassLapse.Models;
using Microsoft.Win32;

namespace ClassLapse.Views;

[SupportedOSPlatform("windows")]
public partial class TimelapseWindow : Window
{
    private sealed record ResolutionOption(string Label, int Height);

    private static readonly ResolutionOption[] Resolutions =
    {
        new("1080p（推荐）", 1080),
        new("720p", 720),
        new("原始（仅同分辨率安全）", 0),
    };

    // Timestamp-resample presets. Minutes = real minutes per kept frame; 0 = keep every frame.
    private sealed record PaceOption(string Label, double Minutes);

    private static readonly PaceOption[] Paces =
    {
        new("完整（每张都用）", 0),
        new("流畅（2 分钟/帧）", 2),
        new("标准·真实等速（5 分钟/帧）", 5),
        new("精简（10 分钟/帧）", 10),
    };

    private sealed class DayRow
    {
        public CheckBox Check = null!;
        public CaptureLibrary.CaptureDay Day = null!;
    }

    private readonly ConfigStore _configStore;
    private AppConfig _config;
    private TimelapseConfig _tl;

    private readonly List<DayRow> _dayRows = new();

    // Per-day frame paths, loaded once in the background so the estimate can reflect the
    // resampled frame count without hitting the disk on every option change.
    private readonly Dictionary<DateOnly, IReadOnlyList<string>> _framesByDate = new();
    private bool _framesLoaded;

    private string? _ffmpegPath;
    private bool _hasLibx264;
    private bool _hasDeflicker;
    private bool _composing;
    private bool _loaded;

    private string _lastComputedOutput = "";
    private bool _userEditedOutput;
    private string? _lastResultPath;
    private CancellationTokenSource? _cts;

    public TimelapseWindow(ConfigStore configStore)
    {
        _configStore = configStore;
        _config = _configStore.Load();
        _tl = _config.Timelapse;

        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var r in Resolutions) ResolutionCombo.Items.Add(r.Label);
        ResolutionCombo.SelectedIndex = IndexForHeight(_tl.ResolutionHeight);
        foreach (var p in Paces) PaceCombo.Items.Add(p.Label);
        PaceCombo.SelectedIndex = IndexForPace(_tl.ResampleMinutes);
        BrightnessCheck.IsChecked = _tl.NormalizeBrightness;
        FpsBox.Text = _tl.Fps.ToString();

        PopulateDays();
        _loaded = true;
        UpdateEstimate();
        _ = LoadFramesForEstimateAsync();

        await DetectFfmpegAsync();
    }

    // ----- days -----

    private void PopulateDays()
    {
        DaysPanel.Children.Clear();
        _dayRows.Clear();

        var days = CaptureLibrary.EnumerateDays(_config.Storage.OutputFolder);
        if (days.Count == 0)
        {
            DaysPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_config.Storage.OutputFolder)
                    ? "未配置输出文件夹（先在设置里选）。"
                    : "输出文件夹里还没有任何日期照片。",
                Foreground = System.Windows.Media.Brushes.Gray,
            });
            return;
        }

        // Latest non-empty day pre-selected; list newest-first so recent days are easy to find.
        var latestNonEmpty = days.LastOrDefault(d => d.JpgCount > 0);
        foreach (var day in days.OrderByDescending(d => d.Date))
        {
            var check = new CheckBox
            {
                Content = $"{day.Date:yyyy-MM-dd}　{day.JpgCount} 张　{day.SizeBytes / 1024.0 / 1024.0:N1} MB",
                Margin = new Thickness(0, 3, 0, 3),
                IsEnabled = day.JpgCount > 0,
                IsChecked = latestNonEmpty != null && day.Date == latestNonEmpty.Date,
            };
            check.Click += (_, _) => UpdateEstimate();
            _dayRows.Add(new DayRow { Check = check, Day = day });
            DaysPanel.Children.Add(check);
        }
    }

    private List<CaptureLibrary.CaptureDay> SelectedDays()
    {
        var list = new List<CaptureLibrary.CaptureDay>();
        foreach (var row in _dayRows)
        {
            if (row.Check.IsChecked == true) list.Add(row.Day);
        }
        return list;
    }

    // Preload each day's frame paths off the UI thread so UpdateEstimate can show the resampled
    // frame count. Names only (no image decoding), so it's cheap even for the dense days.
    private async Task LoadFramesForEstimateAsync()
    {
        try
        {
            var days = _dayRows.Select(r => r.Day).Where(d => d.JpgCount > 0).ToList();
            if (days.Count == 0) { _framesLoaded = true; return; }

            var map = await Task.Run(() =>
            {
                var ordered = days.OrderBy(d => d.Date).ToList();
                var byDay = CaptureLibrary.CollectFramesByDay(ordered); // same ascending order
                var m = new Dictionary<DateOnly, IReadOnlyList<string>>();
                for (int i = 0; i < ordered.Count && i < byDay.Count; i++)
                {
                    m[ordered[i].Date] = byDay[i];
                }
                return m;
            });

            foreach (var kv in map) _framesByDate[kv.Key] = kv.Value;
            _framesLoaded = true;
            UpdateEstimate();
        }
        catch (Exception ex)
        {
            Log.Warn("timelapse: frame preload for estimate failed: " + ex.Message);
        }
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _dayRows) if (row.Check.IsEnabled) row.Check.IsChecked = true;
        UpdateEstimate();
    }

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _dayRows) row.Check.IsChecked = false;
        UpdateEstimate();
    }

    // ----- ffmpeg detection -----

    private async Task DetectFfmpegAsync()
    {
        _ffmpegPath = FfmpegLocator.Find(_tl);
        if (_ffmpegPath == null)
        {
            _hasLibx264 = false;
            _hasDeflicker = false;
            BrightnessCheck.IsEnabled = false;
            FfmpegStatus.Text = "未找到 ffmpeg。把 ffmpeg.exe 放到本程序同目录或系统 PATH，或点右侧手动选择。";
            UpdateComposeEnabled();
            return;
        }

        FfmpegStatus.Text = $"正在检测编码器…（{_ffmpegPath}）";
        UpdateComposeEnabled();
        var composer = new TimelapseComposer(_ffmpegPath);
        try
        {
            _hasLibx264 = await composer.HasEncoderAsync(FfmpegCommand.Libx264);
        }
        catch
        {
            _hasLibx264 = false;
        }
        try
        {
            _hasDeflicker = await composer.HasFilterAsync("deflicker");
        }
        catch
        {
            _hasDeflicker = false;
        }
        BrightnessCheck.IsEnabled = _hasDeflicker && !_composing;
        BrightnessCheck.ToolTip = _hasDeflicker
            ? "消除画面忽明忽暗（webcam 自动曝光、开关灯、投影仪、天光变化）。时间窗平滑，保留白天→傍晚的缓慢明暗变化。"
            : "当前 ffmpeg 构建不含 deflicker 滤镜，无法做亮度统一。";
        FfmpegStatus.Text = _hasLibx264
            ? $"ffmpeg 就绪：{_ffmpegPath}"
            : $"ffmpeg 就绪（无 libx264，将用 mpeg4 兜底，体积偏大）：{_ffmpegPath}";
        UpdateComposeEnabled();
    }

    private void OnPickFfmpegClick(object sender, RoutedEventArgs e)
    {
        if (_composing) return;
        var dlg = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        _tl.FfmpegPath = dlg.FileName;
        SaveConfig();
        _ = DetectFfmpegAsync();
    }

    // ----- options / estimate -----

    private void OnOptionChanged(object sender, SelectionChangedEventArgs e) => UpdateEstimate();
    private void OnOptionChanged(object sender, TextChangedEventArgs e) => UpdateEstimate();

    private void OnOutputManuallyEdited(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        if (OutputBox.Text != _lastComputedOutput) _userEditedOutput = true; // user typed, stop auto-updating it
    }

    private void UpdateEstimate()
    {
        if (!_loaded) return;

        var selected = SelectedDays();
        int fps = ParsedFps();
        double pace = SelectedPaceMinutes();
        int rawFrames = selected.Sum(d => d.JpgCount);
        int frames = EstimatedFrameCount(selected, pace);

        if (!_userEditedOutput)
        {
            var computed = ComputeDefaultOutput(selected);
            if (computed != _lastComputedOutput)
            {
                _lastComputedOutput = computed;
                OutputBox.Text = computed; // fires OnOutputManuallyEdited, but text == _lastComputedOutput so it's ignored
            }
        }

        if (selected.Count == 0)
        {
            EstimateText.Text = "（请勾选至少一个日期）";
        }
        else if (rawFrames == 0)
        {
            EstimateText.Text = "（所选日期没有可用照片）";
        }
        else
        {
            var dur = TimeSpan.FromSeconds(frames / (double)fps);
            string durText = dur.TotalHours >= 1
                ? $"{(int)dur.TotalHours}:{dur.Minutes:00}:{dur.Seconds:00}"
                : $"{dur.Minutes}:{dur.Seconds:00}";
            string frameText = (pace > 0 && _framesLoaded && frames != rawFrames)
                ? $"{rawFrames:N0}→{frames:N0} 帧（重采样）"
                : $"{frames:N0} 帧";
            EstimateText.Text = $"已选 {selected.Count} 天 · {frameText} → 约 {durText} @ {fps} fps";
        }

        UpdateComposeEnabled();
    }

    private int ParsedFps() => int.TryParse(FpsBox.Text, out int f) && f >= 1 ? f : 30;

    private int SelectedHeight()
    {
        int i = ResolutionCombo.SelectedIndex;
        return (i >= 0 && i < Resolutions.Length) ? Resolutions[i].Height : 1080;
    }

    private double SelectedPaceMinutes()
    {
        int i = PaceCombo.SelectedIndex;
        return (i >= 0 && i < Paces.Length) ? Paces[i].Minutes : _tl.ResampleMinutes;
    }

    // Frames that will actually be encoded for the estimate: resampled per-day count when a pace is
    // set and the preload is done, else the raw photo count.
    private int EstimatedFrameCount(List<CaptureLibrary.CaptureDay> selected, double paceMinutes)
    {
        if (paceMinutes <= 0 || !_framesLoaded)
        {
            return selected.Sum(d => d.JpgCount);
        }
        var cadence = TimeSpan.FromMinutes(paceMinutes);
        int sum = 0;
        foreach (var d in selected)
        {
            sum += _framesByDate.TryGetValue(d.Date, out var frames)
                ? FrameResampler.ResampleDay(frames, cadence).Count
                : d.JpgCount;
        }
        return sum;
    }

    private string ComputeDefaultOutput(List<CaptureLibrary.CaptureDay> selected)
    {
        if (selected.Count == 0) return _lastComputedOutput;
        var folder = _config.Storage.OutputFolder ?? "";
        var min = selected.Min(d => d.Date);
        var max = selected.Max(d => d.Date);
        string name = min == max ? $"{min:yyyy-MM-dd}.mp4" : $"{min:yyyy-MM-dd}_to_{max:yyyy-MM-dd}.mp4";
        return string.IsNullOrEmpty(folder) ? name : Path.Combine(folder, name);
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "保存延时视频",
            Filter = "MP4 视频 (*.mp4)|*.mp4",
            FileName = SafeFileName(OutputBox.Text),
            InitialDirectory = SafeDir(OutputBox.Text) ?? _config.Storage.OutputFolder,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _userEditedOutput = true;
            OutputBox.Text = dlg.FileName;
        }
    }

    // ----- compose -----

    private async void OnComposeClick(object sender, RoutedEventArgs e)
    {
        if (_composing) return;
        if (_ffmpegPath == null) { SetStatus("未找到 ffmpeg"); return; }

        var selectedDays = SelectedDays();
        if (selectedDays.Count == 0) { SetStatus("请先勾选日期"); return; }

        int fps = ParsedFps();
        string outPath = OutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outPath)) { SetStatus("请填写输出路径"); return; }
        if (!outPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) outPath += ".mp4";

        try { Directory.CreateDirectory(Path.GetDirectoryName(outPath)!); }
        catch (Exception ex) { SetStatus("输出目录无法创建：" + ex.Message); return; }

        // persist chosen options as next-time defaults (also feeds the run)
        _tl.Fps = fps;
        _tl.ResolutionHeight = SelectedHeight();
        _tl.ResampleMinutes = SelectedPaceMinutes();
        _tl.NormalizeBrightness = BrightnessCheck.IsChecked == true;
        SaveConfig();

        SetComposing(true);
        OpenResultButton.Visibility = Visibility.Collapsed;
        _lastResultPath = null;
        SetStatus("准备帧…");
        Progress.Value = 0;

        var composer = new TimelapseComposer(_ffmpegPath);
        bool hasLibx264 = _hasLibx264;
        bool hasDeflicker = _hasDeflicker;
        var cfg = _tl;
        _cts = new CancellationTokenSource();

        try
        {
            double pace = _tl.ResampleMinutes;
            var frames = await Task.Run<IReadOnlyList<string>>(() =>
                pace > 0
                    ? FrameResampler.Resample(CaptureLibrary.CollectFramesByDay(selectedDays), TimeSpan.FromMinutes(pace))
                    : CaptureLibrary.CollectFrames(selectedDays),
                _cts.Token);
            if (frames.Count == 0) { SetStatus("所选日期没有可用照片"); return; }

            int total = frames.Count;
            var progress = new Progress<double>(p =>
            {
                Progress.Value = p * 100;
                SetStatus($"合成中… {(int)(p * total)} / {total} 帧");
            });

            var result = await composer.ComposeAsync(frames, outPath, cfg, hasLibx264, hasDeflicker, progress, _cts.Token);
            if (result.Success)
            {
                _lastResultPath = result.OutputPath;
                OpenResultButton.Visibility = Visibility.Visible;
                SetStatus($"完成：{Path.GetFileName(result.OutputPath)}（{total} 帧）");
            }
            else
            {
                SetStatus("失败：" + (result.Error ?? $"ffmpeg 退出码 {result.ExitCode}"));
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            SetStatus("出错：" + ex.Message);
            Log.Error("timelapse compose failed", ex);
        }
        finally
        {
            SetComposing(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCancelComposeClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnOpenResultClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastResultPath) || !File.Exists(_lastResultPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastResultPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetStatus("无法打开：" + ex.Message);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel(); // abandon any in-flight compose when the window closes
        base.OnClosing(e);
    }

    // ----- helpers -----

    private void SetComposing(bool on)
    {
        _composing = on;
        Progress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        CancelComposeButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PickFfmpegButton.IsEnabled = !on;
        FpsBox.IsEnabled = !on;
        ResolutionCombo.IsEnabled = !on;
        PaceCombo.IsEnabled = !on;
        BrightnessCheck.IsEnabled = !on && _hasDeflicker;
        OutputBox.IsEnabled = !on;
        foreach (var row in _dayRows) row.Check.IsEnabled = !on && row.Day.JpgCount > 0;
        UpdateComposeEnabled();
    }

    private void UpdateComposeEnabled()
    {
        ComposeButton.IsEnabled = !_composing && _ffmpegPath != null && SelectedDays().Any(d => d.JpgCount > 0);
    }

    private static int IndexForHeight(int height)
    {
        for (int i = 0; i < Resolutions.Length; i++)
        {
            if (Resolutions[i].Height == height) return i;
        }
        return 0; // default 1080p
    }

    private static int IndexForPace(double minutes)
    {
        for (int i = 0; i < Paces.Length; i++)
        {
            if (Paces[i].Minutes == minutes) return i;
        }
        return Array.FindIndex(Paces, p => p.Minutes == 5) is var std && std >= 0 ? std : 0; // fall back to 标准
    }

    private static string? SafeDir(string path)
    {
        try { var d = Path.GetDirectoryName(path); return Directory.Exists(d) ? d : null; }
        catch { return null; }
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return "timelapse.mp4"; }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void SaveConfig()
    {
        try
        {
            // Merge into the latest on-disk config so we don't clobber schedule/pause changes
            // made elsewhere while this window was open.
            var latest = _configStore.Load();
            latest.Timelapse = _tl;
            _configStore.Save(latest);
            _config = latest;
        }
        catch (Exception ex)
        {
            Log.Error("failed to save timelapse config", ex);
        }
    }
}
