using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClassLapse.Core;

/// <summary>
/// Durable JSON-lines audit trail for capture attempts. It lives under AppData so disk and camera
/// failures can still be recorded when the configured output folder is unavailable.
/// </summary>
public sealed class CaptureJournal
{
    public sealed record Entry(
        DateTime Timestamp,
        bool Success,
        string? Path,
        int Width,
        int Height,
        int ByteCount,
        long ElapsedMilliseconds,
        string? Failure,
        string? Error);

    private readonly string _journalDir;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public CaptureJournal(string? journalDir = null)
    {
        _journalDir = journalDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassLapse",
            "capture-journal");
    }

    public string JournalDir => _journalDir;

    public bool TryAppend(Entry entry)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_journalDir);
                string path = Path.Combine(
                    _journalDir,
                    entry.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                string line = JsonSerializer.Serialize(entry, _jsonOptions) + "\n";
                byte[] bytes = Encoding.UTF8.GetBytes(line);

                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("capture journal write failed: " + ex.Message);
            return false;
        }
    }

    public DateTime? FindLastSuccessfulCapture()
    {
        try
        {
            if (!Directory.Exists(_journalDir)) return null;

            foreach (var file in Directory.EnumerateFiles(_journalDir, "*.jsonl")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
            {
                DateTime? latest = null;
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<Entry>(line, _jsonOptions);
                        if (entry is { Success: true }
                            && (latest is null || entry.Timestamp > latest.Value))
                        {
                            latest = entry.Timestamp;
                        }
                    }
                    catch (JsonException)
                    {
                        // A torn or manually edited line must not hide later valid records.
                    }
                }

                if (latest.HasValue) return latest;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("capture journal read failed: " + ex.Message);
        }

        return null;
    }
}
