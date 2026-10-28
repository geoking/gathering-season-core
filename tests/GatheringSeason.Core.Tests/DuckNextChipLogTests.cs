using System.Reflection;
using System.Text.Json;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Reference;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckNextChipLogTests
{
    public static IEnumerable<object[]> WhiteDraws =>
        from definition in DuckRules.V1.EncounterDefinitions.Where(d => d.IsObstacle)
        from protection in new[] { "none", "splash", "guide", "both" }
        select new object[] { definition.DefinitionId, protection };

    [Theory]
    [MemberData(nameof(WhiteDraws))]
    public void Every_white_draw_spends_the_incoming_log_and_resolves_its_own_nuisance(
        string definitionId, string protection)
    {
        var rules = DuckRules.V1;
        var white = Draw(State(log: true, splash: protection is "splash" or "both",
            guide: protection is "guide" or "both", flock: 2), definitionId, rules);
        var suppressed = protection != "none";
        var newLog = definitionId == "fallen_log" && !suppressed;
        Assert.Equal(1, white.Movement);
        Assert.Equal(1, white.State.Exhaustion);
        Assert.Equal(suppressed, white.NuisanceSuppressed);
        Assert.Equal(newLog, white.State.LogSlowdownPending);
        Assert.False(white.State.SplashProtectionArmed);
        Assert.False(white.State.GuideProtectionAvailable);
        Assert.Equal(definitionId == "mud_puddle" && !suppressed ? 1 : 2, white.State.ActiveFlock);
        Assert.Equal(definitionId == "grumpy_goose" && !suppressed ? 4 : 5, white.State.SafeExhaustionMaximum);
        var wish = Draw(white.State, "tailwind_6", rules);
        Assert.Equal(newLog ? 3 : 6, wish.Movement);
        Assert.False(wish.State.LogSlowdownPending);
    }

    [Theory]
    [InlineData("seeds", DuckWorldEventType.RainSoftenedSeeds, 1)]
    [InlineData("tailwind_6", DuckWorldEventType.StillAir, 3)]
    [InlineData("tailwind_4", DuckWorldEventType.HomeBeforeDark, 2)]
    [InlineData("companion", DuckWorldEventType.HomeBeforeDark, 2)]
    [InlineData("signpost", DuckWorldEventType.ThickMorningMist, 1)]
    [InlineData("splash", DuckWorldEventType.HomeBeforeDark, 1)]
    public void Helpful_draw_preserves_bonus_flock_and_single_halving_order(
        string definitionId, DuckWorldEventType weather, int movement)
    {
        var placed = Draw(State(log: true, flock: 1), definitionId, DuckRules.V1, weather);
        Assert.Equal(movement, placed.Movement);
        Assert.False(placed.State.LogSlowdownPending);
        if (definitionId == "companion") Assert.Equal(2, placed.State.ActiveFlock);
        if (definitionId == "splash") Assert.True(placed.State.SplashProtectionArmed);
    }

    [Fact]
    public void Actual_signpost_draw_consumes_log_but_peeks_and_repeated_reads_do_not()
    {
        var runtime = SavedScenario( "fallen_log", "signpost", "tailwind_4", "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match);
        Assert.True(runtime.Player("human").LogSlowdownPending);
        for (var read = 0; read < 4; read++)
        {
            var view = match.GetSnapshot("human");
            DuckDisplayReference.Encounter(view.Rules.Encounter("fallen_log"), view.Rules);
            match.GetLegalActions("human");
            Assert.True(runtime.Player("human").LogSlowdownPending);
        }
        Explore(match);
        Assert.False(runtime.Player("human").LogSlowdownPending);
        Assert.Equal(2, runtime.Player("human").Position);
        Assert.Equal("tailwind_4", match.GetSnapshot("human").KnownNextChips.Single().DefinitionId);
        var captured = JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson);
        for (var read = 0; read < 4; read++) match.GetSnapshot("human");
        Assert.Equal(captured, JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson));
        Explore(match);
        Assert.Equal(6, runtime.Player("human").Position);
    }

    [Fact]
    public void Saved_pending_log_and_saved_consumed_white_continue_with_their_recorded_catalogue()
    {
        var runtime = SavedScenario( "fallen_log", "mud_puddle", "tailwind_4", "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match);
        var pending = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(JsonSerializer.Deserialize<DuckSaveData>(JsonSerializer.Serialize(pending, SaveJson), SaveJson)!);
        Assert.True(restored.GetSnapshot("human").Players.Single(p => p.Id == "human").LogSlowdownPending);
        Explore(match); Explore(restored);
        Assert.False(restored.GetSnapshot("human").Players.Single(p => p.Id == "human").LogSlowdownPending);
        restored = DuckSaves.Restore(DuckSaves.Capture(restored));
        Explore(match); Explore(restored);
        Assert.Equal(6, restored.GetSnapshot("human").Players.Single(p => p.Id == "human").Position);
        Assert.Equal(JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson),
            JsonSerializer.Serialize(DuckSaves.Capture(restored), SaveJson));
    }

    [Theory]
    [MemberData(nameof(WhiteDraws))]
    public void Planner_forecast_matches_actual_white_then_wish_route_(
        string definitionId, string protection)
    {
        var runtime = Scenario( "seeds", definitionId, "tailwind_4");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match); // Planning starts only after a legal first placement.
        var human = runtime.Player("human");
        human.LogSlowdownPending = true;
        human.KnownNextPhysicalChipIds.Add(human.BagPhysicalChipIds[0]);
        human.SplashProtectionArmed = protection is "splash" or "both";
        human.GuideProtectionAvailable = protection is "guide" or "both";
        var before = match.GetSnapshot("human");
        var forecast = PlanValue(before, "ExploreValue");
        Explore(match);
        var stopAfterWhite = PlanValue(match.GetSnapshot("human"), "CurrentValue");
        Explore(match);
        var stopAfterWish = PlanValue(match.GetSnapshot("human"), "CurrentValue");
        Assert.Equal(Math.Max(stopAfterWhite, stopAfterWish), forecast, precision: 8);
    }

    [Fact]
    public void Reference_text_preserves_the_saved_catalogue_log_protection_contract()
    {
        var rules = DuckRules.V1;
        var log = DuckDisplayReference.Encounter(rules.Encounter("fallen_log"), rules);
        var splash = DuckDisplayReference.Encounter(rules.Encounter("splash"), rules);
        var guide = DuckDisplayReference.Event(DuckWorldEventType.FriendlyGuide, rules);
        var stillAir = DuckDisplayReference.Event(DuckWorldEventType.StillAir, rules);
        Assert.Contains("immediately next drawn chip", log);
        Assert.Contains("white Obstacle moves normally", log);
        Assert.Contains("older Log is consumed", splash);
        Assert.Contains("older Log is consumed", guide);
        Assert.Contains("white Obstacle consumes", stillAir);
    }

    private static readonly JsonSerializerOptions SaveJson = new() { IncludeFields = true };
    private static DuckAdventureState State(bool log, bool splash = false, bool guide = false, int flock = 0)
        => new(0, 0, 5, flock, splash, log, guide, false, 0, 0);
    private static DuckAdventurePlacement Draw(DuckAdventureState state, string id, DuckRuleDefinitions rules,
        DuckWorldEventType weather = DuckWorldEventType.HomeBeforeDark)
        => DuckAdventureRules.ApplyEncounter(state, rules.Encounter(id), weather, rules);
    private static void Explore(MatchSession<DuckMatchView> match)
        => match.Execute("human", match.GetLegalActions("human").Single(a => a.Kind == GameActionKind.Explore));
    private static DuckMatchRuntime Scenario(params string[] definitions)
    {
        var runtime = DuckMatchRuntime.Create(901);
        var index = runtime.State.WorldEventDeckDefinitionIds.IndexOf("home_before_dark");
        (runtime.State.WorldEventDeckDefinitionIds[0], runtime.State.WorldEventDeckDefinitionIds[index]) =
            (runtime.State.WorldEventDeckDefinitionIds[index], runtime.State.WorldEventDeckDefinitionIds[0]);
        var player = runtime.Player("human");
        player.Inventory.Clear(); player.BagPhysicalChipIds.Clear(); player.KnownNextPhysicalChipIds.Clear();
        foreach (var id in definitions)
        {
            var physicalId = runtime.State.NextPhysicalChipId++;
            player.Inventory.Add(new DuckPhysicalChipState(physicalId, id));
            player.BagPhysicalChipIds.Add(physicalId);
        }
        player.KnownNextPhysicalChipIds.Add(player.BagPhysicalChipIds[0]);
        return runtime;
    }
    private static DuckMatchRuntime SavedScenario(params string[] definitions)
    {
        var runtime = DuckMatchRuntime.Create(901);
        var index = runtime.State.WorldEventDeckDefinitionIds.IndexOf("home_before_dark");
        (runtime.State.WorldEventDeckDefinitionIds[0], runtime.State.WorldEventDeckDefinitionIds[index]) =
            (runtime.State.WorldEventDeckDefinitionIds[index], runtime.State.WorldEventDeckDefinitionIds[0]);
        var player = runtime.Player("human");
        var ordered = new List<int>();
        foreach (var id in definitions)
        {
            var chip = player.Inventory.FirstOrDefault(c => c.DefinitionId == id && !ordered.Contains(c.PhysicalChipId));
            if (chip == null)
            {
                chip = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, id);
                player.Inventory.Add(chip);
            }
            ordered.Add(chip.PhysicalChipId);
        }
        ordered.AddRange(player.Inventory.Select(c => c.PhysicalChipId).Where(id => !ordered.Contains(id)));
        player.BagPhysicalChipIds.Clear(); player.BagPhysicalChipIds.AddRange(ordered);
        player.KnownNextPhysicalChipIds.Clear();
        return runtime;
    }

    // Compare the policy's private numeric forecast with the same policy's value
    // of actual resulting observations, without adding a production test API.
    private static double PlanValue(DuckMatchView view, string property)
    {
        var plan = typeof(DuckNormalPolicy).GetMethod("PlanAdventure", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { view, view.Players.Single(p => p.Id == "human") })!;
        return (double)plan.GetType().GetProperty(property, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plan)!;
    }
}
