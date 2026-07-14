using System.Globalization;
using System.IO;

namespace ClassLapse.Core;

/// <summary>
/// Persists captures without ever overwriting an existing frame. Bytes are flushed to a unique
/// temporary file in the destination directory, then atomically renamed into place.
/// </summary>
public sealed class CaptureFileStore
{
    private const int MaxCollisionSuffix = 9999;

    public async Task<string> SaveAsync(
        string outputFolder,
        DateTime captureTime,
        ReadOnlyMemory<byte> jpegBytes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
        if (jpegBytes.IsEmpty)
            throw new ArgumentException("JPEG data must not be empty.", nameof(jpegBytes));

        string dayDir = Path.Combine(outputFolder, captureTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dayDir);

        string tempPath = Path.Combine(dayDir, $".capture-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(jpegBytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            string stem = captureTime.ToString("HH-mm-ss-fff", CultureInfo.InvariantCulture);
            for (int suffix = 0; suffix <= MaxCollisionSuffix; suffix++)
            {
                string fileName = suffix == 0 ? $"{stem}.jpg" : $"{stem}-{suffix:00}.jpg";
                string finalPath = Path.Combine(dayDir, fileName);
                try
                {
                    File.Move(tempPath, finalPath, overwrite: false);
                    return finalPath;
                }
                catch (IOException) when (File.Exists(finalPath))
                {
                    // Another capture claimed this name; retry with the next suffix.
                }
            }

            throw new IOException($"Too many capture filename collisions for {stem}.");
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public int CountCapturesForDay(string? outputFolder, DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(outputFolder)) return 0;
        string dayDir = Path.Combine(outputFolder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!Directory.Exists(dayDir)) return 0;

        try
        {
            return Directory.EnumerateFiles(dayDir, "*.jpg", SearchOption.TopDirectoryOnly).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort temp cleanup */ }
    }
}
