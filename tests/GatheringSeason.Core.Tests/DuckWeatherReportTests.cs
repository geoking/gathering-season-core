using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Reference;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckWeatherReportTests
{
    [Fact]
    public void Weather_names_preserve_each_catalogues_stable_identities_and_order()
    {
        var rules = DuckRules.V1;
        var expected = new[]
        {
            ("rain_softened_seeds", 0, "Gentle Rain"),
            ("glorious_sunshine", 10, "Glorious Sunshine"),
            ("a_friendly_guide", 2, "Clearing Breeze"),
            ("a_pocket_of_driftwood", 3, "Windfall Gusts"),
            ("all_tucked_in", 4, "Evening Chill"),
            ("home_before_dark", 5, "Golden Sunset"),
            ("shared_supper", 6, "Morning Dew"),
            ("still_air", 7, "Still Air"),
            ("thick_morning_mist", 8, "Thick Morning Mist"),
            ("restless_night", 9, "Thundery Skies"),
            ("golden_morning", 11, "Golden Morning"),
            ("refreshing_showers", 12, "Refreshing Showers"),
            ("favourable_winds", 13, "Favourable Winds"),
            ("crosswinds", 14, "Crosswinds"),
            ("low_cloud", 15, "Low Cloud")
        };
        Assert.Equal(expected, rules.WeatherReports.Select(report => (report.DefinitionId, (int)report.EventType, report.Name)));
        Assert.Same(rules.WorldEvents, rules.WeatherReports);
        Assert.Equal(15, Enum.GetValues<DuckWorldEventType>().Length);
        foreach (var report in rules.WeatherReports)
        {
            Assert.Same(report, rules.WeatherReport(report.DefinitionId));
            Assert.Same(report, rules.WorldEvent(report.DefinitionId));
            Assert.Equal(DuckDisplayReference.Event(report, rules), DuckDisplayReference.WeatherReport(report, rules));
            Assert.Equal(DuckDisplayReference.Event(report.EventType, rules), DuckDisplayReference.WeatherReport(report.EventType, rules));
        }
    }

    [Fact]
    public void Weather_aliases_leave_save_fields_deck_and_deterministic_continuation_unchanged()
    {
        var match = MatchSession.CreateDuck(42);
        var save = DuckSaves.Capture(match);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var before = JsonSerializer.Serialize(save, options);
        var view = match.GetSnapshot("human");

        Assert.Same(view.CurrentEvent, view.CurrentWeatherReport);
        Assert.Contains(view.CurrentWeatherReport.DefinitionId, save.WorldEventDeckDefinitionIds);
        Assert.Contains("\"WorldEventDeckDefinitionIds\"", before);
        Assert.DoesNotContain("WeatherReport", before);
        Assert.Equal(before, JsonSerializer.Serialize(DuckSaves.Capture(match), options));

        var restored = DuckSaves.Restore(JsonSerializer.Deserialize<DuckSaveData>(before, options)!);
        Assert.Equal(save.WorldEventDeckDefinitionIds, DuckSaves.Capture(restored).WorldEventDeckDefinitionIds);
        Assert.Equal(before, JsonSerializer.Serialize(DuckSaves.Capture(restored), options));
        var action = Assert.Single(match.GetLegalActions("human"));
        var restoredAction = Assert.Single(restored.GetLegalActions("human"));
        Assert.Equal(action.Id, restoredAction.Id);
        Assert.Equal(action.Kind, restoredAction.Kind);
        match.Execute("human", action);
        restored.Execute("human", restoredAction);
        Assert.Equal(JsonSerializer.Serialize(DuckSaves.Capture(match), options),
            JsonSerializer.Serialize(DuckSaves.Capture(restored), options));
        Assert.Equal(8, view.RulesRevision);
    }
}
