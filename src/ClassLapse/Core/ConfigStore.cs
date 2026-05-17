using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassLapse.Models;

namespace ClassLapse.Core;

public sealed class ConfigStore
{
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassLapse",
        "config.json");

    private readonly string _path;
    private readonly JsonSerializerOptions _options;

    public ConfigStore(string? path = null)
    {
        _path = path ?? DefaultConfigPath;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };
    }

    public string FilePath => _path;

    public AppConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new AppConfig();
        }

        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppConfig>(json, _options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            BackupCorrupted();
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string json = JsonSerializer.Serialize(config, _options);

        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }

    private void BackupCorrupted()
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backup = $"{_path}.bak.{stamp}";
            File.Move(_path, backup, overwrite: true);
        }
        catch
        {
            // best-effort
        }
    }
}
