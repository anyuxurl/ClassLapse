namespace ClassLapse.Models;

public sealed class TimelapseConfig
{
    /// <summary>Configured ffmpeg location (the .exe or its folder). Empty = auto-detect beside the app exe / on PATH.</summary>
    public string FfmpegPath { get; set; } = "";

    public int Fps { get; set; } = 30;

    /// <summary>Target output height (width auto-kept even). 0 = original size (even-padded; only safe when all frames share one resolution).</summary>
    public int ResolutionHeight { get; set; } = 1080;

    public int Crf { get; set; } = 23;

    /// <summary>libx264 preset. Default "fast" — classroom IFPs are low-power and "medium" would steal CPU from live capture.</summary>
    public string Preset { get; set; } = "fast";

    /// <summary>
    /// Advanced fallback: some ffmpeg builds ignore the concat demuxer's input <c>-r</c> and lock the output to 25fps.
    /// Set true to switch to the per-file <c>duration</c> list form (and drop the input <c>-r</c>) instead.
    /// </summary>
    public bool UseDurationListFallback { get; set; } = false;
}
