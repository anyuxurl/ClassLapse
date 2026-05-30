using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            if (JsonNode.Parse(json) is JsonObject root)
            {
                // Upgrade a legacy schedule in memory so every (per-tick) load sees v2 entries.
                // Deterministic ids keep this stable across ticks; the disk file is upgraded once
                // at startup via MigrateOnDiskIfNeeded.
                LegacyScheduleMigration.Apply(root);
                return root.Deserialize<AppConfig>(_options) ?? new AppConfig();
            }
            return JsonSerializer.Deserialize<AppConfig>(json, _options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            BackupCorrupted();
            return new AppConfig();
        }
    }

    /// <summary>
    /// One-time on-disk upgrade: if the file carries a legacy (v1) schedule, rewrite it in the v2
    /// shape via the normal atomic <see cref="Save"/>. No-op once migrated. Call once at startup —
    /// deliberately NOT on the per-tick <see cref="Load"/> path, which must not write every second.
    /// </summary>
    public void MigrateOnDiskIfNeeded()
    {
        if (!File.Exists(_path)) return;

        try
        {
            string json = File.ReadAllText(_path);
            if (JsonNode.Parse(json) is not JsonObject root) return;
            if (!LegacyScheduleMigration.Apply(root)) return;

            var upgraded = root.Deserialize<AppConfig>(_options);
            if (upgraded != null)
            {
                Save(upgraded);
                Log.Info("schedule config migrated to v2 entry list");
            }
        }
        catch (JsonException)
        {
            BackupCorrupted();
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
            Log.Warn($"config corrupted, backed up to {backup} and reset to defaults");
        }
        catch
        {
            // best-effort
        }
    }
}
