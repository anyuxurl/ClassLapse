using System.Text.Json.Nodes;
using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class LegacyScheduleMigrationTests
{
    private static JsonObject LegacyRoot() => new()
    {
        ["Schedule"] = new JsonObject
        {
            ["ActiveDays"] = new JsonArray("Monday", "Wednesday"),
            ["TimeWindows"] = new JsonArray(
                new JsonObject { ["Start"] = "08:00:00", ["End"] = "11:30:00" },
                new JsonObject { ["Start"] = "13:30:00", ["End"] = "17:00:00" }),
            ["IntervalSeconds"] = 45,
        },
    };

    [Fact]
    public void Apply_converts_legacy_windows_to_interval_entries()
    {
        var root = LegacyRoot();

        bool migrated = LegacyScheduleMigration.Apply(root);

        Assert.True(migrated);

        var schedule = (JsonObject)root["Schedule"]!;
        Assert.Null(schedule["TimeWindows"]);
        Assert.Null(schedule["ActiveDays"]);
        Assert.Null(schedule["IntervalSeconds"]);
        Assert.Equal(AppConfig.CurrentSchemaVersion, (int)root["SchemaVersion"]!);

        var entries = (JsonArray)schedule["Entries"]!;
        Assert.Equal(2, entries.Count);

        var e0 = (JsonObject)entries[0]!;
        Assert.Equal("legacy-0", (string)e0["Id"]!);
        Assert.Equal(nameof(ScheduleMode.Interval), (string)e0["Mode"]!);
        Assert.Equal(45, (int)e0["IntervalSeconds"]!);
        Assert.Equal("08:00:00", (string)((JsonObject)e0["Window"]!)["Start"]!);
        Assert.Equal("legacy-1", (string)((JsonObject)entries[1]!)["Id"]!);
    }

    [Fact]
    public void Apply_is_noop_when_entries_already_present()
    {
        var root = new JsonObject
        {
            ["Schedule"] = new JsonObject
            {
                ["Entries"] = new JsonArray(),
                ["TimeWindows"] = new JsonArray(
                    new JsonObject { ["Start"] = "08:00:00", ["End"] = "09:00:00" }),
            },
        };

        bool migrated = LegacyScheduleMigration.Apply(root);

        Assert.False(migrated);
        Assert.Empty((JsonArray)((JsonObject)root["Schedule"]!)["Entries"]!); // explicit empty honoured
    }

    [Fact]
    public void Apply_is_noop_when_no_schedule()
    {
        var root = new JsonObject { ["Camera"] = new JsonObject { ["JpegQuality"] = 70 } };

        Assert.False(LegacyScheduleMigration.Apply(root));
    }

    [Fact]
    public void Apply_twice_is_idempotent()
    {
        var root = LegacyRoot();

        Assert.True(LegacyScheduleMigration.Apply(root));
        Assert.False(LegacyScheduleMigration.Apply(root)); // Entries now present → no-op
    }

    [Fact]
    public void Apply_omits_active_days_when_legacy_has_none()
    {
        var root = new JsonObject
        {
            ["Schedule"] = new JsonObject
            {
                ["TimeWindows"] = new JsonArray(
                    new JsonObject { ["Start"] = "08:00:00", ["End"] = "09:00:00" }),
                ["IntervalSeconds"] = 20,
            },
        };

        Assert.True(LegacyScheduleMigration.Apply(root));

        var e0 = (JsonObject)((JsonArray)((JsonObject)root["Schedule"]!)["Entries"]!)[0]!;
        Assert.False(e0.ContainsKey("ActiveDays")); // ScheduleEntry default (Mon–Fri) applies on deserialize
        Assert.Equal(20, (int)e0["IntervalSeconds"]!);
    }
}
