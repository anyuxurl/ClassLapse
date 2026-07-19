namespace ClassLapse.Models;

public sealed class TimelapseConfig
{
    /// <summary>Configured ffmpeg location (the .exe or its folder). Empty = auto-detect beside the app exe / on PATH.</summary>
    public string FfmpegPath { get; set; } = "";

    public int Fps { get; set; } = 30;

    /// <summary>
    /// Timestamp-aware pacing: keep ~one frame per this many real minutes within each day, so output
    /// screen-time tracks real elapsed time instead of how many photos happened to be captured.
    /// 0 = off (use every frame — the raw "one photo = one output frame" behaviour). Default 5
    /// ("标准·真实等速") tames days captured at very short intervals without touching sparse days.
    /// </summary>
    public double ResampleMinutes { get; set; } = 5;

    /// <summary>Target output height (width auto-kept even). 0 = original size (even-padded; only safe when all frames share one resolution).</summary>
    public int ResolutionHeight { get; set; } = 1080;

    /// <summary>
    /// Brightness unification (deflicker): even out frame-to-frame luminance swings — webcam
    /// auto-exposure, lights toggling, projector glow, daylight through windows — so the timelapse
    /// doesn't strobe. Applied as ffmpeg's temporal <c>deflicker</c> filter over a small sliding
    /// window, which smooths the jitter while still letting slow real trends (dawn→noon→dusk)
    /// through. On by default; skipped automatically when the ffmpeg build lacks the filter.
    /// </summary>
    public bool NormalizeBrightness { get; set; } = true;

    public int Crf { get; set; } = 23;

    /// <summary>libx264 preset. Default "fast" — classroom IFPs are low-power and "medium" would steal CPU from live capture.</summary>
    public string Preset { get; set; } = "fast";

    /// <summary>
    /// Advanced fallback: some ffmpeg builds ignore the concat demuxer's input <c>-r</c> and lock the output to 25fps.
    /// Set true to switch to the per-file <c>duration</c> list form (and drop the input <c>-r</c>) instead.
    /// </summary>
    public bool UseDurationListFallback { get; set; } = false;
}
