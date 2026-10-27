using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckWishSetTwoTests
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    [Fact]
    public void Seeds_use_the_immediately_previous_obstacle_before_weather_and_Log()
    {
        var rules = DuckRules.Set2;
        var initial = new DuckAdventureState(0, 0, 5, 0, false, false, true, false, 0, 0);
        var protectedObstacle = DuckAdventureRules.ApplyEncounter(initial, rules.Encounter("brambles"), DuckWorldEventType.FriendlyGuide, rules);
        Assert.True(protectedObstacle.NuisanceSuppressed);
        var afterObstacle = protectedObstacle.State;
        Assert.Equal(2, DuckAdventureRules.ApplyEncounter(afterObstacle, rules.Encounter("seeds"), DuckWorldEventType.HomeBeforeDark, rules).Movement);
        var afterLog = new DuckAdventureState(1, 1, 5, 0, false, true, false, false, 0, 0, true);
        Assert.Equal(2, DuckAdventureRules.ApplyEncounter(afterLog, rules.Encounter("seeds"), DuckWorldEventType.RainSoftenedSeeds, rules).Movement);
        var afterWish = new DuckAdventureState(1, 1, 5, 0, false, false, false, false, 0, 0);
        Assert.Equal(1, DuckAdventureRules.ApplyEncounter(afterWish, rules.Encounter("seeds"), DuckWorldEventType.HomeBeforeDark, rules).Movement);
    }

    [Fact]
    public void Splash_prevents_next_obstacle_exhaustion_but_Goose_nuisance_can_still_wear_out()
    {
        var rules = DuckRules.Set2;
        var armed = new DuckAdventureState(1, 5, 5, 0, true, false, false, false, 0, 0);
        var goose = DuckAdventureRules.ApplyEncounter(armed, rules.Encounter("grumpy_goose"), DuckWorldEventType.HomeBeforeDark, rules);
        Assert.Equal(5, goose.State.Exhaustion);
        Assert.Equal(4, goose.State.SafeExhaustionMaximum);
        Assert.True(goose.WearsOut);
        Assert.False(goose.NuisanceSuppressed);
        Assert.False(goose.State.SplashProtectionArmed);
        var breeze = new DuckAdventureState(1, 5, 5, 0, true, false, true, false, 0, 0);
        var protectedGoose = DuckAdventureRules.ApplyEncounter(breeze, rules.Encounter("grumpy_goose"), DuckWorldEventType.FriendlyGuide, rules);
        Assert.False(protectedGoose.WearsOut);
        Assert.True(protectedGoose.NuisanceSuppressed);
        var helpful = DuckAdventureRules.ApplyEncounter(armed, rules.Encounter("seeds"), DuckWorldEventType.HomeBeforeDark, rules);
        Assert.False(helpful.State.SplashProtectionArmed);
        var showers = DuckAdventureRules.ApplyEncounter(armed, rules.Encounter("splash"), DuckWorldEventType.RefreshingShowers, rules);
        Assert.Equal(4, showers.State.Exhaustion);
        Assert.True(showers.State.SplashProtectionArmed);
    }

    [Fact]
    public void Companion_moves_one_and_Mud_lowers_only_the_active_flock()
    {
        var rules = DuckRules.Set2;
        var state = new DuckAdventureState(0, 0, 5, 0, false, false, false, false, 0, 0);
        var companion = DuckAdventureRules.ApplyEncounter(state, rules.Encounter("companion"), DuckWorldEventType.HomeBeforeDark, rules);
        Assert.Equal(1, companion.Movement);
        Assert.Equal(1, companion.State.ActiveFlock);
        var second = DuckAdventureRules.ApplyEncounter(companion.State, rules.Encounter("companion"), DuckWorldEventType.FavourableWinds, rules);
        Assert.Equal(2, second.Movement);
        Assert.Equal(2, second.State.ActiveFlock);
        var mud = DuckAdventureRules.ApplyEncounter(second.State, rules.Encounter("mud_puddle"), DuckWorldEventType.HomeBeforeDark, rules);
        Assert.Equal(1, mud.State.ActiveFlock);
        Assert.Equal(4, mud.State.Position);
    }

    [Fact]
    public void Reeds_wait_for_Night_and_preview_includes_only_shelter_qualified_twigs()
    {
        var runtime = Runtime();
        var player = runtime.Player("human");
        PutFirst(runtime, "human", "seeds", "seeds", "seeds", "reeds_3");
        var match = new MatchSession<DuckMatchView>(runtime);
        for (var draw = 0; draw < 4; draw++) Execute(match, "human", GameActionKind.Explore);
        Assert.Equal(4, player.Position);
        Assert.Equal(0, player.TotalTwigs);
        Assert.Equal(0, player.DayReedsTwigs);
        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(runtime.Rules.BoardSpaceAt(4).Twigs + 3, preview.NestTwigChangeOnRest);
        FinishOther(runtime);
        Execute(match, "human", GameActionKind.Settle);
        Assert.Equal(3, player.LastNightOutcome!.ReedsTwigs);
        Assert.Equal(preview.NestTwigChangeOnRest, player.TotalTwigs);
    }

    [Theory]
    [InlineData(4, true, "wildflowers", 3, 2)]
    [InlineData(3, false, "wildflowers", 0, 2)]
    [InlineData(4, false, "seeds", 3, 0)]
    public void Night_uses_final_shelter_and_final_chip_and_awards_tied_flocks_before_halving(
        int position, bool wornOut, string finalDefinition, int reeds, int flowers)
    {
        var runtime = Runtime();
        foreach (var player in runtime.State.Players)
        {
            PlaceFinal(runtime, player, position, "reeds_3", finalDefinition);
            player.ActiveFlock = 2;
        }
        runtime.Player("human").IsWornOut = wornOut;
        var outcomes = DuckNightResolver.Calculate(runtime.State, runtime.Rules);
        var own = outcomes["human"];
        Assert.Equal(reeds, own.ReedsTwigs);
        Assert.Equal(flowers, own.FlowerSleep);
        Assert.Equal(2, own.FlockSleep);
        Assert.Equal(2, outcomes["ai"].FlockSleep);
        Assert.Equal(wornOut ? own.SleepBeforeWear / 2 : own.SleepBeforeWear, own.FrozenSleep);
    }

    [Fact]
    public void Signpost_pair_is_unordered_then_overlap_retains_deduced_order_and_save_knowledge()
    {
        var runtime = Runtime();
        PutFirst(runtime, "human", "signpost", "signpost", "tailwind_4", "reeds_3");
        var match = new MatchSession<DuckMatchView>(runtime);
        Execute(match, "human", GameActionKind.Explore);
        var view = match.GetSnapshot("human");
        Assert.Empty(view.KnownNextChips);
        Assert.Equal(2, view.UnorderedNextChips.Count);
        Assert.Equal(new[] { "signpost", "tailwind_4" }, view.DrawProbability!.Outcomes.Select(outcome => outcome.DefinitionId));
        Assert.All(view.DrawProbability.Outcomes, outcome => Assert.Equal(0.5, outcome.Probability));
        var odds = JsonSerializer.Serialize(view.DrawProbability);
        var bag = runtime.Player("human").BagPhysicalChipIds;
        (bag[0], bag[1]) = (bag[1], bag[0]);
        Assert.Equal(odds, JsonSerializer.Serialize(match.GetSnapshot("human").DrawProbability));
        (bag[0], bag[1]) = (bag[1], bag[0]);
        var before = Serialize(DuckSaves.Capture(match));
        Assert.Equal(before, Serialize(DuckSaves.Capture(match)));
        var restored = DuckSaves.Restore(DuckSaves.Capture(match));
        Assert.Equal(view.UnorderedNextChips.Select(chip => chip.PhysicalChipId), restored.GetSnapshot("human").UnorderedNextChips.Select(chip => chip.PhysicalChipId));
        Execute(match, "human", GameActionKind.Explore);
        Execute(restored, "human", GameActionKind.Explore);
        view = match.GetSnapshot("human");
        Assert.Empty(view.UnorderedNextChips);
        Assert.Equal(new[] { "tailwind_4", "reeds_3" }, view.KnownNextChips.Select(chip => chip.DefinitionId));
        Assert.Equal(1, Assert.Single(view.DrawProbability!.Outcomes).Probability);
        Assert.Equal(Serialize(DuckSaves.Capture(match)), Serialize(DuckSaves.Capture(restored)));
        Assert.Empty(match.GetSnapshot("ai").UnorderedNextChips);
    }

    [Theory]
    [InlineData("thick_morning_mist", false, false, 0, 0)]
    [InlineData("home_before_dark", true, false, 1, 0)]
    [InlineData("home_before_dark", false, true, 0, 2)]
    public void Signpost_handles_Mist_one_chip_left_and_identical_pairs(
        string report, bool oneLeft, bool identical, int orderedCount, int unorderedCount)
    {
        var runtime = Runtime();
        var index = runtime.State.WorldEventDeckDefinitionIds.IndexOf(report);
        (runtime.State.WorldEventDeckDefinitionIds[0], runtime.State.WorldEventDeckDefinitionIds[index]) =
            (runtime.State.WorldEventDeckDefinitionIds[index], runtime.State.WorldEventDeckDefinitionIds[0]);
        PutFirst(runtime, "human", "signpost", "seeds", identical ? "seeds" : "tailwind_4");
        var player = runtime.Player("human");
        player.BagPhysicalChipIds.RemoveRange(oneLeft ? 2 : 3, player.BagPhysicalChipIds.Count - (oneLeft ? 2 : 3));
        var match = new MatchSession<DuckMatchView>(runtime);
        Execute(match, "human", GameActionKind.Explore);
        var view = match.GetSnapshot("human");
        Assert.Equal(orderedCount, view.KnownNextChips.Count);
        Assert.Equal(unorderedCount, view.UnorderedNextChips.Count);
        Assert.Equal(oneLeft || identical ? 1 : 0.5, view.DrawProbability!.Outcomes.Single(outcome => outcome.DefinitionId == "seeds").Probability);
        if (identical)
        {
            Execute(match, "human", GameActionKind.Explore);
            Assert.Single(match.GetSnapshot("human").KnownNextChips);
            Assert.Empty(match.GetSnapshot("human").UnorderedNextChips);
            Assert.Equal(1, Assert.Single(match.GetSnapshot("human").DrawProbability!.Outcomes).Probability);
        }
    }

    [Fact]
    public void Probability_uses_only_lawful_composition_and_authoritative_wear_out_projection()
    {
        var runtime = Runtime();
        var player = runtime.Player("human");
        var match = new MatchSession<DuckMatchView>(runtime);
        var before = DuckSaves.Capture(match);
        var initial = match.GetSnapshot("human").DrawProbability!;
        Assert.Equal(1, initial.Outcomes.Sum(outcome => outcome.Probability), 10);
        var seeds = initial.Outcomes.Single(outcome => outcome.DefinitionId == "seeds");
        Assert.Equal(2, seeds.Numerator);
        Assert.Equal(13, seeds.Denominator);
        var distribution = JsonSerializer.Serialize(initial);
        player.BagPhysicalChipIds.Reverse();
        runtime.Player("ai").BagPhysicalChipIds.Reverse();
        Assert.Equal(distribution, JsonSerializer.Serialize(match.GetSnapshot("human").DrawProbability));
        Assert.Equal(before.Random.State, DuckSaves.Capture(match).Random.State);
        player.Exhaustion = 5;
        player.SplashProtectionArmed = true;
        PutFirst(runtime, "human", "grumpy_goose");
        player.KnownNextPhysicalChipIds.Add(player.BagPhysicalChipIds[0]);
        var chance = match.GetSnapshot("human").DrawProbability!;
        Assert.Equal(1, chance.WearOutProbability);
        player.GuideProtectionAvailable = true;
        Assert.Equal(0, match.GetSnapshot("human").DrawProbability!.WearOutProbability);
    }

    [Fact]
    public void Tailwind_reserves_unplaced_chip_and_restores_both_choices_without_consuming_randomness()
    {
        var runtime = Runtime();
        PutFirst(runtime, "human", "seeds", "tailwind_4");
        var match = new MatchSession<DuckMatchView>(runtime);
        Execute(match, "human", GameActionKind.Explore);
        var before = DuckSaves.Capture(match);
        Execute(match, "human", GameActionKind.Explore);
        var pending = match.GetSnapshot("human");
        Assert.Single(pending.Players[0].PlacedChips);
        Assert.Equal(1, pending.Players[0].Position);
        Assert.Equal(12, pending.OwnBag.Count);
        Assert.Null(pending.RestPreview);
        Assert.Null(pending.DrawProbability);
        Assert.Equal(4, pending.OwnPendingTailwind!.ShelterPosition);
        Assert.Equal(before.Random.State, DuckSaves.Capture(match).Random.State);
        var full = DuckSaves.Restore(DuckSaves.Capture(match));
        var shelter = DuckSaves.Restore(DuckSaves.Capture(match));
        Choose(full, "human", "duck.tailwind.full");
        Choose(shelter, "human", "duck.tailwind.shelter");
        Assert.Equal(5, full.GetSnapshot("human").Players[0].Position);
        Assert.False(full.GetSnapshot("human").Players[0].HasFinishedDay);
        Assert.Equal(4, shelter.GetSnapshot("human").Players[0].Position);
        Assert.True(shelter.GetSnapshot("human").Players[0].HasFinishedDay);
        Assert.Null(shelter.GetSnapshot("human").OwnPendingTailwind);
        var state = new DuckAdventureState(0, 0, 5, 0, false, true, false, false, 0, 0);
        var shortened = DuckAdventureRules.ApplyEncounter(state, DuckRules.Set2.Encounter("tailwind_6"), DuckWorldEventType.StillAir, DuckRules.Set2);
        Assert.Equal(3, shortened.Movement);
        Assert.Null(DuckAdventureRules.FirstReachableShelter(0, shortened.State.Position, DuckRules.Set2));
        DuckSaves.Validate(DuckSaves.Capture(full));
        DuckSaves.Validate(DuckSaves.Capture(shelter));
    }

    [Fact]
    public void Final_day_cohort_remains_frozen_until_every_Tailwind_choice_completes_and_resumes()
    {
        var runtime = Runtime(4);
        var match = new MatchSession<DuckMatchView>(runtime);
        AdvanceToFinalDay(match);
        foreach (var player in runtime.State.Players) PutFirst(runtime, player.Id, "tailwind_6");
        var frozenPositions = runtime.State.Players.Select(player => player.Position).ToArray();
        var frozenHistory = runtime.State.History.Count;
        foreach (var player in runtime.State.Players) Execute(match, player.Id, GameActionKind.Explore);
        Assert.Equal(frozenPositions, runtime.State.Players.Select(player => player.Position));
        Assert.Equal(frozenHistory, runtime.State.History.Count);
        Assert.All(runtime.State.Players, player => Assert.Empty(player.PlacedChips));
        var forged = DuckSaves.Capture(match);
        forged.Players[1].PendingTailwindPhysicalChipId = 0;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(forged));
        var missingCohort = DuckSaves.Capture(match);
        missingCohort.FinalDayCommits.Clear();
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(missingCohort));
        var resumed = DuckSaves.Restore(DuckSaves.Capture(match));
        foreach (var player in runtime.State.Players.Take(3))
        {
            Choose(match, player.Id, "duck.tailwind.full");
            Choose(resumed, player.Id, "duck.tailwind.full");
            Assert.Equal(frozenPositions, runtime.State.Players.Select(candidate => candidate.Position));
            Assert.Equal(frozenHistory, runtime.State.History.Count);
        }
        var midway = DuckSaves.Restore(DuckSaves.Capture(match));
        Choose(match, "ai-3", "duck.tailwind.full");
        Choose(resumed, "ai-3", "duck.tailwind.full");
        Choose(midway, "ai-3", "duck.tailwind.full");
        Assert.All(runtime.State.Players, player => Assert.Single(player.PlacedChips));
        Assert.Equal(2, runtime.State.FinalDayDecisionBeat);
        Assert.Equal(Serialize(DuckSaves.Capture(match)), Serialize(DuckSaves.Capture(resumed)));
        Assert.Equal(Serialize(DuckSaves.Capture(match)), Serialize(DuckSaves.Capture(midway)));
    }

    [Fact]
    public void Final_day_mixed_draw_and_settle_wait_for_Tailwind_and_full_landing_does_not_force_settle()
    {
        var runtime = Runtime(3);
        var match = new MatchSession<DuckMatchView>(runtime);
        AdvanceToFinalDay(match);
        runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex] = "home_before_dark";
        foreach (var player in runtime.State.Players)
        {
            player.PermanentFeatherTrail = player.EffectiveStart = player.Position = 1;
            player.ActiveMostRestedStep = false;
            PutFirst(runtime, player.Id, "seeds", player.Id == "human" ? "tailwind_2" : "seeds");
        }
        foreach (var player in runtime.State.Players) Execute(match, player.Id, GameActionKind.Explore);
        var historyCount = runtime.State.History.Count;
        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "ai", GameActionKind.Explore);
        Execute(match, "ai-2", GameActionKind.Settle);
        Assert.All(runtime.State.Players, player => Assert.Equal(2, player.Position));
        Assert.All(runtime.State.Players, player => Assert.Single(player.PlacedChips));
        Assert.All(runtime.State.Players, player => Assert.False(player.HasFinishedDay));
        Assert.Equal(historyCount, runtime.State.History.Count);
        var pending = match.GetSnapshot("human").OwnPendingTailwind!;
        Assert.Equal(4, pending.FullPosition);
        Assert.Equal(pending.FullPosition, pending.ShelterPosition);
        Choose(match, "human", "duck.tailwind.full");
        Assert.Equal(4, runtime.Player("human").Position);
        Assert.False(runtime.Player("human").HasFinishedDay);
        Assert.Equal(3, runtime.Player("ai").Position);
        Assert.True(runtime.Player("ai-2").HasFinishedDay);
        Assert.Equal(3, runtime.State.FinalDayDecisionBeat);
    }

    [Fact]
    public void Night_purchases_use_current_prices_and_restore_the_same_Stars_and_receipt()
    {
        var runtime = Runtime();
        PlaceFinal(runtime, runtime.Player("human"), 4, "seeds");
        FinishOther(runtime);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        var match = new MatchSession<DuckMatchView>(runtime);
        Assert.Equal(3, runtime.Player("human").RemainingSleep);
        var actions = match.GetLegalActions("human");
        Assert.Equal(2, actions.Single(action => action.DefinitionId == "tailwind_4").Cost);
        Assert.DoesNotContain(actions, action => action.DefinitionId == "reeds_3");
        var purchase = actions.Single(action => action.DefinitionId == "reeds_2");
        Assert.Equal(3, purchase.Cost);
        match.Execute("human", purchase);

        var save = DuckSaves.Capture(match);
        Assert.Equal(2, save.Settings.WishSetVersion);
        var restored = DuckSaves.Restore(save);
        var buyer = restored.GetSnapshot("human").Players.Single(player => player.Id == "human");
        Assert.Equal(0, buyer.RemainingStars);
        Assert.Equal("reeds_2", Assert.Single(buyer.PurchasedEncounterDefinitionIds));
        Assert.DoesNotContain(restored.GetLegalActions("human"), action => action.Kind == GameActionKind.BuyEncounter);
        Assert.Equal(Serialize(save), Serialize(DuckSaves.Capture(restored)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Unsupported_Second_set_versions_fail_before_state_validation(int version)
    {
        Assert.Throws<ArgumentException>(() => new DuckMatchSettings(2, "set-2", version));
        var save = DuckSaves.Capture(MatchSession.CreateDuck(991, new DuckMatchSettings(2, "set-2")));
        save.Settings.WishSetVersion = version;
        save.Random = null!;
        var error = Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        Assert.Contains("Settings.WishSetVersion", error.Message);
    }

    [Fact]
    public void Saves_reject_unknown_sets_and_forged_pending_choices_and_keep_legacy_First_set()
    {
        var match = MatchSession.CreateDuck(991, new DuckMatchSettings(2, "set-2"));
        var save = DuckSaves.Capture(match);
        save.Settings.WishSetId = "set-3";
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        save.Settings.WishSetId = "set-2";
        save.Players[0].PendingTailwindPhysicalChipId = save.Players[0].Inventory.First(chip => chip.DefinitionId == "seeds").PhysicalChipId;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        var legacy = DuckSaves.Capture(MatchSession.CreateDuck(992));
        legacy.Settings.WishSetId = null;
        legacy.Settings.WishSetVersion = 0;
        Assert.Equal("set-1", DuckSaves.Restore(legacy).GetSnapshot("human").Settings.WishSetId);
    }

    private static DuckMatchRuntime Runtime(int players = 2)
    {
        var runtime = DuckMatchRuntime.Create(991, new DuckMatchSettings(players, "set-2"));
        var desired = runtime.State.WorldEventDeckDefinitionIds.IndexOf("home_before_dark");
        (runtime.State.WorldEventDeckDefinitionIds[0], runtime.State.WorldEventDeckDefinitionIds[desired]) =
            (runtime.State.WorldEventDeckDefinitionIds[desired], runtime.State.WorldEventDeckDefinitionIds[0]);
        foreach (var player in runtime.State.Players) player.GuideProtectionAvailable = false;
        return runtime;
    }

    private static void PutFirst(DuckMatchRuntime runtime, string playerId, params string[] definitions)
    {
        var player = runtime.Player(playerId);
        var ids = player.BagPhysicalChipIds.Where(id => player.Inventory.Single(chip => chip.PhysicalChipId == id).DefinitionId != "grumpy_goose").Take(definitions.Length).ToArray();
        foreach (var id in ids) player.BagPhysicalChipIds.Remove(id);
        player.BagPhysicalChipIds.InsertRange(0, ids);
        for (var index = 0; index < definitions.Length; index++)
        {
            var inventoryIndex = player.Inventory.FindIndex(chip => chip.PhysicalChipId == ids[index]);
            player.Inventory[inventoryIndex] = new DuckPhysicalChipState(ids[index], definitions[index]);
        }
    }

    private static void FinishOther(DuckMatchRuntime runtime) => PlaceFinal(runtime, runtime.Player("ai"), 1, "seeds");

    private static void PlaceFinal(DuckMatchRuntime runtime, DuckPlayerState player, int position, params string[] definitions)
    {
        PutFirst(runtime, player.Id, definitions);
        foreach (var id in player.BagPhysicalChipIds.Take(definitions.Length).ToArray())
        {
            player.BagPhysicalChipIds.Remove(id);
            player.PlacedChips.Add(new DuckPlacedChipState(id, position, false));
            player.PlacedHelpfulTypes.Add(runtime.Rules.Encounter(player.Inventory.Single(chip => chip.PhysicalChipId == id).DefinitionId).EncounterType);
        }
        player.Position = position;
        player.HasFinishedDay = true;
    }

    private static void AdvanceToFinalDay(MatchSession<DuckMatchView> match)
    {
        while (match.GetSnapshot("human").Day < 10)
        {
            var view = match.GetSnapshot("human");
            foreach (var player in view.Players)
            {
                var choice = match.GetLegalActions(player.Id).FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit);
                if (choice != null) match.Execute(player.Id, choice);
            }
            foreach (var player in view.Players)
            {
                Execute(match, player.Id, GameActionKind.Explore);
                if (match.GetLegalActions(player.Id).Any(action => action.Kind == GameActionKind.ChooseTailwind)) Choose(match, player.Id, "duck.tailwind.full");
                if (match.GetLegalActions(player.Id).Any(action => action.Kind == GameActionKind.Settle)) Execute(match, player.Id, GameActionKind.Settle);
            }
            foreach (var player in view.Players) Execute(match, player.Id, GameActionKind.FinishDream);
            Execute(match, "human", GameActionKind.NextDay);
        }
        foreach (var player in match.GetSnapshot("human").Players)
        {
            var choice = match.GetLegalActions(player.Id).FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit);
            if (choice != null) match.Execute(player.Id, choice);
        }
    }

    private static void Execute(MatchSession<DuckMatchView> match, string playerId, GameActionKind kind) =>
        match.Execute(playerId, match.GetLegalActions(playerId).Single(action => action.Kind == kind));
    private static void Choose(MatchSession<DuckMatchView> match, string playerId, string actionId) =>
        match.Execute(playerId, match.GetLegalActions(playerId).Single(action => action.Id == actionId));
    private static string Serialize(DuckSaveData save) => JsonSerializer.Serialize(save, Json);
}
