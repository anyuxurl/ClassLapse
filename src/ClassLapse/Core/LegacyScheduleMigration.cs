using System.Text.Json.Nodes;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Upgrades a v1 (pre-entry) schedule to the v2 entry-list shape at the JSON level, in place.
/// <para>v1: <c>Schedule = { ActiveDays, TimeWindows[], IntervalSeconds }</c> — one global window list and interval.</para>
/// <para>v2: <c>Schedule = { Entries[] }</c> — one <see cref="ScheduleEntry"/> per former window.</para>
/// Deterministic ids (<c>legacy-{i}</c>) make this idempotent: once <c>Entries</c> exists, a repeat apply is a no-op,
/// so it is safe to run on every per-tick load without churning the scheduler's per-entry timing.
/// </summary>
public static class LegacyScheduleMigration
{
    /// <summary>
    /// Mutates <paramref name="root"/> (a parsed <see cref="AppConfig"/> object) in place; returns true if it
    /// changed anything. Migrates only when there is no <c>Entries</c> array but a legacy <c>TimeWindows</c>
    /// array is present — an explicit (even empty) <c>Entries</c> array is honoured as-is.
    /// </summary>
    public static bool Apply(JsonObject root)
    {
        if (root["Schedule"] is not JsonObject schedule) return false;
        if (schedule["Entries"] is JsonArray) return false;                  // already v2 (incl. explicit [])
        if (schedule["TimeWindows"] is not JsonArray legacyWindows) return false;

        var activeDaysNode = schedule["ActiveDays"];
        var intervalNode = schedule["IntervalSeconds"];

        var entries = new JsonArray();
        for (int i = 0; i < legacyWindows.Count; i++)
        {
            if (legacyWindows[i] is not JsonObject window) continue;

            var entry = new JsonObject
            {
                ["Id"] = $"legacy-{i}",
                ["Enabled"] = true,
                ["Name"] = "",
                ["Mode"] = nameof(ScheduleMode.Interval),
                ["Window"] = window.DeepClone(),
                ["IntervalSeconds"] = intervalNode?.DeepClone() ?? JsonValue.Create(30),
                ["Times"] = new JsonArray(),
            };
            // Omit ActiveDays when absent so the ScheduleEntry default (Mon–Fri) applies instead of null.
            if (activeDaysNode is not null) entry["ActiveDays"] = activeDaysNode.DeepClone();

            entries.Add(entry);
        }

        schedule["Entries"] = entries;
        schedule.Remove("TimeWindows");
        schedule.Remove("ActiveDays");
        schedule.Remove("IntervalSeconds");
        root["SchemaVersion"] = AppConfig.CurrentSchemaVersion;
        return true;
    }
}
