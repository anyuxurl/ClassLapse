using System.IO;

namespace ClassLapse.Core;

/// <summary>
/// Append-only per-day rolling text logger. Writes
/// <c>%AppData%\ClassLapse\logs\YYYY-MM-DD.log</c> by default.
/// All write failures are swallowed — logging must never crash the app.
/// </summary>
public sealed class Logger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public Logger(string? logDir = null)
    {
        _logDir = logDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassLapse",
            "logs");
        try { Directory.CreateDirectory(_logDir); } catch { /* ignore */ }
    }

    public string LogDir => _logDir;

    public string TodayLogPath => Path.Combine(_logDir, $"{DateTime.Today:yyyy-MM-dd}.log");

    public void Info(string msg) => Write("INFO", msg);

    public void Warn(string msg) => Write("WARN", msg);

    public void Error(string msg, Exception? ex = null)
        => Write("ERROR", ex == null ? msg : msg + Environment.NewLine + ex);

    private void Write(string level, string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}";
        try
        {
            lock (_lock)
            {
                File.AppendAllText(TodayLogPath, line);
            }
        }
        catch
        {
            // best-effort
        }
    }
}

/// <summary>
/// Static facade for application-wide logging. Call <see cref="SetInstance"/>
/// once at startup to point at the real log directory; default uses %AppData%.
/// </summary>
public static class Log
{
    private static Logger _instance = new();

    public static Logger Instance => _instance;

    public static void SetInstance(Logger logger) => _instance = logger;

    public static void Info(string msg) => _instance.Info(msg);

    public static void Warn(string msg) => _instance.Warn(msg);

    public static void Error(string msg, Exception? ex = null) => _instance.Error(msg, ex);
}
