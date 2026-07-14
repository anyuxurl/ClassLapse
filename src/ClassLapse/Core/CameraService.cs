using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ClassLapse.Core;

[SupportedOSPlatform("windows")]
public sealed class CameraService
{
    public enum FailureReason { None, DeviceBusy, DeviceNotFound, Timeout, EncodingFailed, Unknown }

    public sealed record CaptureResult(
        bool Success,
        byte[]? JpegBytes,
        int Width,
        int Height,
        long ElapsedMilliseconds,
        FailureReason Failure,
        string? ErrorMessage);

    private static readonly ImageCodecInfo JpegEncoder =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private readonly TimeSpan _captureTimeout;
    private readonly TimeSpan _stopTimeout;

    public CameraService(TimeSpan? captureTimeout = null, TimeSpan? stopTimeout = null)
    {
        _captureTimeout = captureTimeout ?? TimeSpan.FromSeconds(4);
        _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(2);
    }

    public async Task<CaptureResult> TryCaptureAsync(
        string monikerString,
        int desiredWidth,
        int desiredHeight,
        int jpegQuality,
        bool useHighestResolution = false,
        Action<Bitmap>? beforeEncode = null,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        VideoCaptureDevice device;
        try
        {
            device = new VideoCaptureDevice(monikerString);
            // VideoCapabilities is lazily populated by the driver; touching it forces enumeration.
            if (device.VideoCapabilities == null || device.VideoCapabilities.Length == 0)
            {
                return Failed(sw, FailureReason.DeviceNotFound, "Device has no video capabilities (driver missing or device disconnected).");
            }
        }
        catch (Exception ex)
        {
            return Failed(sw, FailureReason.DeviceNotFound, $"Cannot instantiate device: {ex.Message}");
        }

        SelectResolution(device, desiredWidth, desiredHeight, useHighestResolution);

        var tcs = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? deviceErrorDescription = null;

        NewFrameEventHandler onNewFrame = (sender, args) =>
        {
            try
            {
                var clone = (Bitmap)args.Frame.Clone();
                if (!tcs.TrySetResult(clone)) clone.Dispose();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        VideoSourceErrorEventHandler onVideoSourceError = (sender, args) =>
        {
            deviceErrorDescription = args.Description;
            tcs.TrySetResult(null);
        };

        device.NewFrame += onNewFrame;
        device.VideoSourceError += onVideoSourceError;

        Bitmap? frame = null;
        try
        {
            try
            {
                device.Start();
            }
            catch (Exception ex)
            {
                return Failed(sw, FailureReason.DeviceBusy, $"Cannot start device (likely busy): {ex.Message}");
            }

            using var ctReg = ct.Register(() => tcs.TrySetCanceled(ct));
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(_captureTimeout));

            if (winner != tcs.Task)
            {
                return Failed(sw, FailureReason.Timeout, $"No frame within {_captureTimeout.TotalSeconds:0.#}s (device may be busy).");
            }

            try
            {
                frame = await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failed(sw, FailureReason.Unknown, $"Frame delivery failed: {ex.Message}");
            }

            if (frame == null)
            {
                var msg = deviceErrorDescription ?? "Device reported an error.";
                return Failed(sw, FailureReason.DeviceBusy, msg);
            }
        }
        finally
        {
            device.NewFrame -= onNewFrame;
            device.VideoSourceError -= onVideoSourceError;
            SafeStop(device);
        }

        try
        {
            // Last touch before encoding — e.g. burn a timestamp watermark onto the frame.
            beforeEncode?.Invoke(frame);
            byte[] jpeg = EncodeJpeg(frame, jpegQuality);
            int w = frame.Width, h = frame.Height;
            sw.Stop();
            return new CaptureResult(true, jpeg, w, h, sw.ElapsedMilliseconds, FailureReason.None, null);
        }
        catch (Exception ex)
        {
            return Failed(sw, FailureReason.EncodingFailed, $"JPEG encode failed: {ex.Message}");
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private static void SelectResolution(VideoCaptureDevice device, int targetWidth, int targetHeight, bool useHighest)
    {
        var caps = device.VideoCapabilities;
        if (caps == null || caps.Length == 0) return;

        VideoCapabilities best = caps[0];

        if (useHighest)
        {
            int bestArea = 0;
            foreach (var c in caps)
            {
                int area = c.FrameSize.Width * c.FrameSize.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = c;
                }
            }
        }
        else
        {
            int bestScore = int.MaxValue;
            foreach (var c in caps)
            {
                int dw = c.FrameSize.Width - targetWidth;
                int dh = c.FrameSize.Height - targetHeight;
                int score = (dw * dw) + (dh * dh);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
        }
        device.VideoResolution = best;
    }

    private void SafeStop(VideoCaptureDevice device)
    {
        try
        {
            if (!device.IsRunning) return;

            device.SignalToStop();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (device.IsRunning && sw.Elapsed < _stopTimeout)
            {
                Thread.Sleep(25);
            }

            if (!device.IsRunning) return;

            Log.Warn($"camera did not stop within {_stopTimeout.TotalSeconds:0.#}s; forcing stop in background");
            _ = Task.Run(() =>
            {
                try { device.Stop(); }
                catch (Exception ex) { Log.Warn("camera force-stop failed: " + ex.Message); }
            });
        }
        catch (Exception ex)
        {
            Log.Warn("camera stop failed: " + ex.Message);
        }
    }

    private static byte[] EncodeJpeg(Bitmap bmp, int quality)
    {
        var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        using var ms = new MemoryStream();
        bmp.Save(ms, JpegEncoder, encParams);
        return ms.ToArray();
    }

    private static CaptureResult Failed(System.Diagnostics.Stopwatch sw, FailureReason reason, string message)
    {
        sw.Stop();
        return new CaptureResult(false, null, 0, 0, sw.ElapsedMilliseconds, reason, message);
    }
}
