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
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Weather_names_preserve_each_catalogues_stable_identities_and_order(int revision)
    {
        var rules = DuckRules.ForRulesRevision(revision);
        var retained = new[]
        {
            ("rain_softened_seeds", 0, "Gentle Rain"),
            revision < 3 ? ("sunlit_signboards", 1, "Clear Sunlight") : ("glorious_sunshine", 10, "Glorious Sunshine"),
            ("a_friendly_guide", 2, "Clearing Breeze"),
            ("a_pocket_of_driftwood", 3, "Windfall Gusts"),
            ("all_tucked_in", 4, "Evening Chill"),
            ("home_before_dark", 5, "Golden Sunset"),
            ("shared_supper", 6, "Morning Dew"),
            ("still_air", 7, "Still Air"),
            ("thick_morning_mist", 8, "Thick Morning Mist"),
            ("restless_night", 9, "Thundery Skies")
        };

        var expected = revision < 8 ? retained : retained.Concat(new[]
        {
            ("golden_morning", 11, "Golden Morning"),
            ("refreshing_showers", 12, "Refreshing Showers"),
            ("favourable_winds", 13, "Favourable Winds"),
            ("crosswinds", 14, "Crosswinds"),
            ("low_cloud", 15, "Low Cloud")
        });
        Assert.Equal(expected, rules.WeatherReports.Select(report => (report.DefinitionId, (int)report.EventType, report.Name)));
        Assert.Same(rules.WorldEvents, rules.WeatherReports);
        Assert.Equal(16, Enum.GetValues<DuckWorldEventType>().Length);
        foreach (var report in rules.WeatherReports)
        {
            Assert.Same(report, rules.WeatherReport(report.DefinitionId));
            Assert.Same(report, rules.WorldEvent(report.DefinitionId));
            Assert.Equal(DuckDisplayReference.Event(report, rules), DuckDisplayReference.WeatherReport(report, rules));
            Assert.Equal(DuckDisplayReference.Event(report.EventType, rules), DuckDisplayReference.WeatherReport(report.EventType, rules));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Weather_reference_preserves_legacy_thresholds_shelter_gates_and_Log_rules(int revision)
    {
        var rules = DuckRules.ForRulesRevision(revision);
        var sunshine = DuckDisplayReference.WeatherReport(rules.WeatherReport("glorious_sunshine"), rules);
        Assert.Contains(revision < 6 ? "8 is safe, or 7 after an unprotected Goose" : "7 is safe, or 6 after an unprotected Goose", sunshine);
        Assert.Contains(revision < 4 ? "+8 Sleep" : "+2 Stars", sunshine);
        Assert.Contains(revision < 5 ? "every duck finishes safely at a shelter" : "every duck finishes at a shelter",
            DuckDisplayReference.WeatherReport(rules.WeatherReport("all_tucked_in"), rules));
        var thunder = DuckDisplayReference.WeatherReport(rules.WeatherReport("restless_night"), rules);
        Assert.Contains("Thunder keeps shelter ducks awake", thunder);
        Assert.Contains(revision < 5 ? "finishes safely at a shelter" : "finishes at a shelter", thunder);
        Assert.Contains("printed shelter reward, Flowers, and Final Night shelter bonus together", thunder);
        Assert.Contains(revision < 8 ? "older Log slowdown remains" : "older Log is consumed by that draw",
            DuckDisplayReference.WeatherReport(rules.WeatherReport("a_friendly_guide"), rules));
        Assert.Contains(revision < 8 ? "Log's next Wish" : "immediately next drawn chip",
            DuckDisplayReference.WeatherReport(rules.WeatherReport("still_air"), rules));
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
