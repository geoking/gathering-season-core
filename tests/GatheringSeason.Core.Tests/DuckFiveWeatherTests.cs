using System.Reflection;
using System.Text.Json;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckFiveWeatherTests
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    [Theory]
    [InlineData("seeds")]
    [InlineData("tailwind_2")]
    [InlineData("tailwind_4")]
    [InlineData("tailwind_6")]
    [InlineData("reeds_1")]
    [InlineData("reeds_2")]
    [InlineData("reeds_3")]
    [InlineData("signpost")]
    [InlineData("splash")]
    [InlineData("companion")]
    [InlineData("wildflowers")]
    public void Golden_Morning_rewards_exactly_the_first_Wish_after_white_chips(string firstWish)
    {
        var white = Draw(State(), "fallen_log", DuckWorldEventType.GoldenMorning);
        Assert.Equal(0, white.EventTwigsAwarded);
        Assert.Equal(0, white.State.HelpfulTypeMask);
        var first = Draw(white.State, firstWish, DuckWorldEventType.GoldenMorning);
        Assert.Equal(1, first.EventTwigsAwarded);
        Assert.Equal(0, Draw(first.State, "reeds_3", DuckWorldEventType.GoldenMorning).EventTwigsAwarded);
    }

    [Fact]
    public void Golden_Morning_is_per_duck_and_survives_wear_out_with_one_final_Brambles_deduction()
    {
        var runtime = Scenario("golden_morning", "seeds", "brambles");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human"); Explore(match, "ai");
        Assert.All(runtime.State.Players, p => Assert.Equal(1, p.DayEventTwigs));
        Assert.Equal(2, runtime.State.History.Count(h => h.Message.Contains("1 Twig from Golden Morning")));
        foreach (var p in runtime.State.Players) p.Exhaustion = p.SafeExhaustionMaximum;
        Explore(match, "human"); Explore(match, "ai");
        Assert.Equal(DuckPhase.Night, runtime.State.Phase);
        Assert.All(runtime.State.Players, p =>
        {
            Assert.True(p.IsWornOut);
            Assert.Equal(1, p.LastNightOutcome!.EventTwigs);
            Assert.Equal(1, p.LastNightOutcome.BramblesPenalty);
            Assert.Equal(p.LastNightOutcome.PrintedTwigs, p.TotalTwigs);
        });
    }

    [Fact]
    public void Golden_Morning_and_Crosswinds_peeks_do_not_consume_triggers()
    {
        foreach (var weather in new[] { "golden_morning", "crosswinds" })
        {
            var runtime = Scenario(weather, "signpost", "tailwind_6", "seeds");
            var match = new MatchSession<DuckMatchView>(runtime);
            Explore(match, "human");
            Assert.Equal("tailwind_6", match.GetSnapshot("human").KnownNextChips.Single().DefinitionId);
            var before = (runtime.Player("human").Position, runtime.Player("human").DayEventTwigs, runtime.Player("human").PlacedChips.Count);
            for (var i = 0; i < 4; i++) { match.GetSnapshot("human"); match.GetLegalActions("human"); }
            Assert.Equal(before, (runtime.Player("human").Position, runtime.Player("human").DayEventTwigs, runtime.Player("human").PlacedChips.Count));
            Explore(match, "human");
            Assert.Equal(weather == "crosswinds" ? 7 : 8, runtime.Player("human").Position);
            Assert.Equal(weather == "golden_morning" ? 1 : 0, runtime.Player("human").DayEventTwigs);
        }
        var unseen = Scenario("golden_morning", "fallen_log", "seeds", "splash");
        var unseenMatch = new MatchSession<DuckMatchView>(unseen);
        unseen.Player("human").KnownNextPhysicalChipIds.AddRange(unseen.Player("human").BagPhysicalChipIds.Take(2));
        unseenMatch.GetSnapshot("human");
        Assert.Equal(0, unseen.Player("human").DayEventTwigs);
        Explore(unseenMatch, "human"); Explore(unseenMatch, "human");
        Assert.Equal(1, unseen.Player("human").DayEventTwigs);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(4, 3)]
    public void Showers_recovers_current_exhaustion_keeps_Goose_limit_and_next_chip_protection(int exhaustion, int expected)
    {
        var result = Draw(State(exhaustion: exhaustion, maximum: 4), "splash", DuckWorldEventType.RefreshingShowers);
        Assert.Equal(expected, result.State.Exhaustion);
        Assert.Equal(4, result.State.SafeExhaustionMaximum);
        Assert.True(result.State.SplashProtectionArmed);
        var goose = Draw(result.State, "grumpy_goose", DuckWorldEventType.RefreshingShowers);
        Assert.True(goose.NuisanceSuppressed);
        Assert.Equal(4, goose.State.SafeExhaustionMaximum);
        Assert.Equal(expected + 1, goose.State.Exhaustion);
        Assert.False(goose.State.SplashProtectionArmed);
    }

    [Fact]
    public void Showers_cannot_revive_a_worn_out_duck()
    {
        var runtime = Scenario("refreshing_showers", "grumpy_goose", "splash");
        runtime.Player("human").Exhaustion = 5;
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");
        Assert.True(runtime.Player("human").IsWornOut);
        Assert.DoesNotContain(match.GetLegalActions("human"), a => a.Kind == GameActionKind.Explore);
        Assert.Single(runtime.Player("human").BagPhysicalChipIds);
    }

    public static IEnumerable<object[]> WindMovements =>
        from d in DuckRules.V1.EncounterDefinitions
        from log in new[] { false, true }
        select new object[] { d.DefinitionId, log };

    [Theory]
    [MemberData(nameof(WindMovements))]
    public void Favourable_Winds_applies_after_normal_movement_before_Log_and_caps_at_oasis(string id, bool log)
    {
        var definition = DuckRules.V1.Encounter(id);
        var state = State(position: 42, flock: 3, log: log);
        var normal = definition.EncounterType == DuckEncounterType.Companion ? 4 : definition.BaseMovement!.Value;
        var expected = normal + (definition.IsHelpful ? 1 : 0);
        if (log && definition.IsHelpful) expected = (expected + 1) / 2;
        var result = Draw(state, id, DuckWorldEventType.FavourableWinds);
        Assert.Equal(expected, result.Movement);
        Assert.Equal(43, result.State.Position);
        if (id == "companion") Assert.Equal(4, result.State.ActiveFlock);
    }

    [Theory]
    [InlineData("tailwind_2", 1, 1)]
    [InlineData("tailwind_4", 3, 2)]
    [InlineData("tailwind_6", 5, 3)]
    public void Crosswinds_shares_first_trigger_across_variants_and_precedes_Log(string id, int normal, int logged)
    {
        Assert.Equal(normal, Draw(State(), id, DuckWorldEventType.Crosswinds).Movement);
        var first = Draw(State(log: true), id, DuckWorldEventType.Crosswinds);
        Assert.Equal(logged, first.Movement);
        Assert.Equal(6, Draw(first.State, "tailwind_6", DuckWorldEventType.Crosswinds).Movement);
        var otherWish = Draw(State(), "seeds", DuckWorldEventType.Crosswinds);
        Assert.Equal(normal, Draw(otherWish.State, id, DuckWorldEventType.Crosswinds).Movement);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Low_Cloud_preserves_tied_Most_Rested_recognition_preview_Stars_and_Feathers_without_benefit(int day)
    {
        var runtime = Scenario("low_cloud", "splash", "seeds");
        runtime.State.Day = day;
        foreach (var p in runtime.State.Players)
        {
            var chip = p.Inventory.First(c => c.DefinitionId == "splash");
            p.BagPhysicalChipIds.Remove(chip.PhysicalChipId);
            p.PlacedChips.Add(new DuckPlacedChipState(chip.PhysicalChipId, 17, false));
            p.PlacedHelpfulTypes.Add(DuckEncounterType.Splash);
            p.Position = 17;
            p.HasFinishedDay = true;
        }
        var preview = DuckNightResolver.Preview(runtime.State, runtime.Rules, runtime.Player("human"))!;
        Assert.Equal(DuckRestBonusStatus.Guaranteed, preview.MostRestedStatus);
        var outcomes = DuckNightResolver.Calculate(runtime.State, runtime.Rules);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        Assert.All(outcomes.Values, o =>
        {
            Assert.True(o.IsMostRested);
            Assert.Equal(0, o.NextDayTemporaryStep);
            Assert.Equal(day == 10 ? o.FrozenSleep : 0, o.DreamTwigs);
            Assert.Equal(runtime.Rules.BoardSpaceAt(17).Feathers, o.FeathersAwarded);
        });
        Assert.All(runtime.State.Players, p => Assert.False(p.PendingMostRestedStep));
        Assert.Equal(2, runtime.State.PublicAwards.Count(a => a.DefinitionId == "most_rested"));
        Assert.Equal(outcomes["human"].DreamTwigs, preview.DreamTwigsMinimum);
        Assert.Equal(preview.DreamTwigsMinimum, preview.DreamTwigsMaximum);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_fifteen_card_deck_plays_only_ten_unique_days_and_Continue_preserves_every_action(bool lowCloudFinalDay)
    {
        var runtime = DuckMatchRuntime.Create(211);
        if (lowCloudFinalDay)
        {
            var index = runtime.State.WorldEventDeckDefinitionIds.IndexOf("low_cloud");
            (runtime.State.WorldEventDeckDefinitionIds[9], runtime.State.WorldEventDeckDefinitionIds[index]) =
                (runtime.State.WorldEventDeckDefinitionIds[index], runtime.State.WorldEventDeckDefinitionIds[9]);
        }
        var match = new MatchSession<DuckMatchView>(runtime);
        var originalDeck = DuckSaves.Capture(match).WorldEventDeckDefinitionIds.ToArray();
        Assert.Equal(15, originalDeck.Distinct().Count());
        var seen = new Dictionary<int, string>();
        for (var step = 0; step < 500; step++)
        {
            var view = match.GetSnapshot("human");
            seen[view.Day] = view.CurrentEvent.DefinitionId;
            var restored = DuckSaves.Restore(DuckSaves.Capture(match));
            Assert.Equal(Serialize(match), Serialize(restored));
            if (view.Phase == DuckPhase.Finished) break;
            var found = false;
            foreach (var p in view.Players)
            {
                var actions = match.GetLegalActions(p.Id);
                var action = actions.FirstOrDefault(a => a.Kind == GameActionKind.ChooseEventBenefit)
                    ?? actions.FirstOrDefault(a => a.Kind == GameActionKind.Settle)
                    ?? actions.FirstOrDefault(a => a.Kind == GameActionKind.Explore)
                    ?? actions.FirstOrDefault(a => a.Kind == GameActionKind.FinishDream)
                    ?? actions.FirstOrDefault(a => a.Kind == GameActionKind.NextDay);
                if (action == null) continue;
                var continuation = restored.GetLegalActions(p.Id).Single(a => a.Id == action.Id);
                match.Execute(p.Id, action); restored.Execute(p.Id, continuation);
                Assert.Equal(Serialize(match), Serialize(restored));
                found = true; break;
            }
            Assert.True(found);
        }
        Assert.Equal(DuckPhase.Finished, match.GetSnapshot("human").Phase);
        Assert.Equal(10, seen.Count);
        Assert.Equal(originalDeck.Take(10), seen.OrderBy(pair => pair.Key).Select(pair => pair.Value));
        Assert.Equal(5, originalDeck.Except(seen.Values).Count());
        Assert.Equal(originalDeck, DuckSaves.Capture(match).WorldEventDeckDefinitionIds);
        if (lowCloudFinalDay)
        {
            var invalid = DuckSaves.Capture(match);
            invalid.Players.First(p => p.LastNightOutcome!.IsMostRested).LastNightOutcome!.DreamTwigs++;
            Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(invalid));
        }
    }

    [Theory]
    [InlineData("favourable_winds", 15, 17, 0)]
    [InlineData("golden_morning", 14, 16, 2)]
    public void Optimistic_final_recovery_bound_includes_unclaimed_weather_gain(
        string weather, int position, int opponentPosition, int opponentBank)
    {
        var runtime = FinalPlanningScenario(weather, position, opponentPosition, opponentBank, "seeds");
        Assert.True(PlanFlag(runtime, "CurrentRestProvablyLoses"));
        Assert.True(PlanFlag(runtime, "OptimisticRecoveryPossible"));
        runtime.State.WorldEventDeckDefinitionIds[0] = "thick_morning_mist";
        Assert.False(PlanFlag(runtime, "OptimisticRecoveryPossible"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Low_Cloud_policy_values_no_Most_Rested_benefit(int day)
    {
        var runtime = FinalPlanningScenario("low_cloud", 15, 15, 1, "seeds");
        runtime.State.Day = day;
        var view = runtime.GetSnapshot("human");
        var value = (double)typeof(DuckNormalPolicy).GetMethod("MostRestedValue", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { view, view.Players.Single(p => p.Id == "human"), 20, false })!;
        Assert.Equal(0, value);
        if (day != 10) return;
        Assert.True(PlanFlag(runtime, "CurrentRestProvablyLoses"));
        runtime.State.WorldEventDeckDefinitionIds[0] = "thick_morning_mist";
        Assert.False(PlanFlag(runtime, "CurrentRestProvablyLoses"));
    }

    private static bool PlanFlag(DuckMatchRuntime runtime, string property)
    {
        var view = runtime.GetSnapshot("human");
        var plan = typeof(DuckNormalPolicy).GetMethod("PlanAdventure", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { view, view.Players.Single(p => p.Id == "human") })!;
        return (bool)plan.GetType().GetProperty(property, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plan)!;
    }

    private static DuckMatchRuntime FinalPlanningScenario(string weather, int position, int opponentPosition,
        int opponentBank, string remaining)
    {
        var runtime = Scenario(weather, "mud_puddle", remaining);
        runtime.State.Day = 10;
        runtime.State.FinalDayDecisionBeat = 1;
        foreach (var p in runtime.State.Players)
        {
            var chip = p.Inventory.First(c => c.DefinitionId == "mud_puddle");
            p.BagPhysicalChipIds.Remove(chip.PhysicalChipId);
            p.Position = p.Id == "human" ? position : opponentPosition;
            p.PlacedChips.Add(new DuckPlacedChipState(chip.PhysicalChipId, p.Position, false));
            p.Exhaustion = 1;
            p.HasFinishedDay = p.Id != "human";
            p.TotalTwigs = p.Id == "human" ? 0 : opponentBank;
        }
        return runtime;
    }

    private static DuckAdventureState State(int position = 0, int exhaustion = 0, int maximum = 5, int flock = 0, bool log = false)
        => new(position, exhaustion, maximum, flock, false, log, false, false, 0, 0);
    private static DuckAdventurePlacement Draw(DuckAdventureState state, string id, DuckWorldEventType weather)
        => DuckAdventureRules.ApplyEncounter(state, DuckRules.V1.Encounter(id), weather, DuckRules.V1);
    private static void Explore(MatchSession<DuckMatchView> match, string id)
        => match.Execute(id, match.GetLegalActions(id).Single(a => a.Kind == GameActionKind.Explore));
    private static string Serialize(MatchSession<DuckMatchView> match) => JsonSerializer.Serialize(DuckSaves.Capture(match), Json);
    private static DuckMatchRuntime Scenario(string weather, params string[] definitions)
    {
        var runtime = DuckMatchRuntime.Create(211);
        var index = runtime.State.WorldEventDeckDefinitionIds.IndexOf(weather);
        (runtime.State.WorldEventDeckDefinitionIds[0], runtime.State.WorldEventDeckDefinitionIds[index]) =
            (runtime.State.WorldEventDeckDefinitionIds[index], runtime.State.WorldEventDeckDefinitionIds[0]);
        foreach (var p in runtime.State.Players)
        {
            p.Inventory.Clear(); p.BagPhysicalChipIds.Clear(); p.KnownNextPhysicalChipIds.Clear();
            p.GuideProtectionAvailable = false;
            foreach (var id in definitions)
            {
                var physical = runtime.State.NextPhysicalChipId++;
                p.Inventory.Add(new DuckPhysicalChipState(physical, id)); p.BagPhysicalChipIds.Add(physical);
            }
        }
        return runtime;
    }
}
