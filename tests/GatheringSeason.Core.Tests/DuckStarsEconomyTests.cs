using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckStarsEconomyTests
{

    [Fact]
    public void combines_each_approved_Star_bonus_once()
    {
        var runtime = Runtime(day: 10, eventId: "glorious_sunshine");
        Complete(runtime, "human", position: 17, flowers: 3, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        Complete(runtime, "ai", position: 17, flowers: 1, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);

        var outcomes = DuckNightResolver.Calculate(runtime.State, runtime.Rules);
        var human = outcomes["human"];

        Assert.Equal(3, human.PrintedReward);
        Assert.Equal(1, human.FlowerReward);
        Assert.Equal(1, human.FinalShelterReward);
        Assert.Equal(2, human.GloriousSunshineReward);
        Assert.Equal(1, human.FlockReward);
        Assert.Equal(8, human.RewardBeforeWear);
        Assert.Equal(8, human.FrozenReward);
        Assert.Equal(9, human.DreamTwigs);
        Assert.True(human.IsMostRested);
    }

    [Theory]
    [InlineData("all_tucked_in", 4, 10)]
    [InlineData("home_before_dark", 1, 5)]
    [InlineData("shared_supper", 1, 5)]
    public void collective_events_award_exactly_one_Star_to_each_eligible_duck(
        string eventId,
        int humanPosition,
        int aiPosition)
    {
        var runtime = Runtime(day: 1, eventId);
        Complete(runtime, "human", humanPosition);
        Complete(runtime, "ai", aiPosition);

        var outcomes = DuckNightResolver.Calculate(runtime.State, runtime.Rules);

        Assert.All(outcomes.Values, outcome => Assert.Equal(1, outcome.CollectiveEventReward));
        Assert.Equal(runtime.Rules.BoardSpaceAt(humanPosition).Reward + 1, outcomes["human"].FrozenReward);
        Assert.Equal(runtime.Rules.BoardSpaceAt(aiPosition).Reward + 1, outcomes["ai"].FrozenReward);
    }

    [Fact]
    public void Free_Seed_uses_one_slot_once_and_Normal_then_finishes()
    {
        var runtime = DuckMatchRuntime.Create(seed: 4002);
        runtime.State.Phase = DuckPhase.Night;
        foreach (var player in runtime.State.Players)
        {
            player.FrozenSleep = 0;
            player.RemainingSleep = 0;
            player.IsSleepFrozen = true;
            player.HasFinishedDream = false;
        }
        var match = new MatchSession<DuckMatchView>(runtime);
        var policy = new DuckNormalPolicy();

        var first = policy.Choose(match.GetSnapshot("human"), match.GetLegalActions("human"));
        Assert.Equal(GameActionKind.BuyEncounter, first.Kind);
        Assert.Equal("seeds", first.DefinitionId);
        Assert.Equal(0, first.Cost);
        Assert.Contains("Wish Seeds", first.Label, StringComparison.Ordinal);
        match.Execute("human", first);

        var actions = match.GetLegalActions("human");
        Assert.DoesNotContain(actions, action => action.Kind == GameActionKind.BuyEncounter);
        Assert.Equal(GameActionKind.FinishDream,
            policy.Choose(match.GetSnapshot("human"), actions).Kind);
        Assert.Equal(0, runtime.Player("human").RemainingSleep);
        Assert.Equal("seeds", Assert.Single(runtime.Player("human").PurchasedEncounterDefinitionIds));
    }

    [Fact]
    public void save_round_trip_keeps_Stars_rules_and_zero_price_purchase()
    {
        var runtime = ResolvedDayOneSunshine();
        var match = new MatchSession<DuckMatchView>(runtime);
        var seed = match.GetLegalActions("human").Single(action => action.DefinitionId == "seeds");
        match.Execute("human", seed);

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save);
        var view = restored.GetSnapshot("human");

        Assert.Equal(8, save.RulesVersion);
        Assert.Equal(8, view.RulesRevision);
        Assert.True(view.Economy.UsesStars);
        Assert.Equal(view.Players.Single(player => player.Id == "human").FrozenReward,
            view.Players.Single(player => player.Id == "human").RemainingReward);
        Assert.Contains("seeds", view.Players.Single(player => player.Id == "human").PurchasedEncounterDefinitionIds);
        Assert.DoesNotContain(restored.GetLegalActions("human"), action => action.DefinitionId == "seeds");
    }

    [Fact]
    public void resolved_worn_shelter_rewards_round_trip_without_reinterpretation()
    {
        var runtime = Runtime(day: 10, eventId: "restless_night");
        Complete(runtime, "human", position: 17, flowers: 2, flock: 2, wornOut: true);
        Complete(runtime, "ai", position: 5, flock: 1);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        var match = new MatchSession<DuckMatchView>(runtime);

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save).GetSnapshot("human");
        var player = restored.Players.Single(candidate => candidate.Id == "human");
        var outcome = Assert.IsType<DuckNightOutcome>(player.LastNightOutcome);

        Assert.Equal(8, save.RulesVersion);
        Assert.Equal(1, player.PermanentFeatherTrail);
        Assert.Equal(1, outcome.FlowerReward);
        Assert.Equal(1, outcome.FinalShelterReward);
        Assert.Equal(1, outcome.RestlessNightPenalty);
        Assert.Equal(1, outcome.FlockReward);
        Assert.Equal(2, outcome.FrozenReward);
        Assert.False(outcome.IsMostRested);
    }

    [Fact]
    public void Save_validation_uses_current_bonus_conversion_and_purchase_parameters()
    {
        var runtime = ResolvedDayOneSunshine();
        var match = new MatchSession<DuckMatchView>(runtime);
        match.Execute("human", match.GetLegalActions("human").Single(action => action.DefinitionId == "seeds"));

        var sunshine = DuckSaves.Capture(match);
        sunshine.Players[0].LastNightOutcome!.GloriousSunshineSleep = 8;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(sunshine));

        var flowers = DuckSaves.Capture(match);
        flowers.Players[0].LastNightOutcome!.FlowerSleep = 2;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(flowers));

        var flock = DuckSaves.Capture(match);
        flock.Players[0].LastNightOutcome!.FlockSleep = 2;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(flock));

        var purchase = DuckSaves.Capture(match);
        purchase.Players[0].RemainingSleep--;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(purchase));

        var finalRuntime = Runtime(day: 10, eventId: "rain_softened_seeds");
        Complete(finalRuntime, "human", position: 15);
        Complete(finalRuntime, "ai", position: 1);
        DuckNightResolver.Resolve(finalRuntime.State, finalRuntime.Rules);
        var finalMatch = new MatchSession<DuckMatchView>(finalRuntime);
        var validFinal = DuckSaves.Capture(finalMatch);
        Assert.Equal(validFinal.Players[0].FrozenSleep + 1, validFinal.Players[0].LastNightOutcome!.DreamTwigs);
        Assert.Equal(validFinal.Players[1].FrozenSleep, validFinal.Players[1].LastNightOutcome!.DreamTwigs);
        DuckSaves.Restore(validFinal);

        var wrongConversion = DuckSaves.Capture(finalMatch);
        wrongConversion.Players[0].LastNightOutcome!.DreamTwigs--;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(wrongConversion));
    }

    private static DuckMatchRuntime ResolvedDayOneSunshine()
    {
        var runtime = Runtime(day: 1, eventId: "glorious_sunshine");
        Complete(runtime, "human", position: 4, flowers: 2, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        Complete(runtime, "ai", position: 4, flowers: 1, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        return runtime;
    }

    private static DuckMatchRuntime Runtime(int day, string eventId)
    {
        var runtime = DuckMatchRuntime.Create(seed: 4000 + day);
        runtime.State.Day = day;
        runtime.State.CurrentEventIndex = day - 1;
        runtime.State.FinalDayDecisionBeat = day == 10 ? 1 : 0;
        if (day >= 5)
        {
            runtime.State.DayFiveGooseAdded = true;
            foreach (var player in runtime.State.Players)
            {
                var goose = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, "grumpy_goose");
                player.Inventory.Add(goose);
                player.BagPhysicalChipIds.Add(goose.PhysicalChipId);
            }
        }
        var eventIndex = runtime.State.WorldEventDeckDefinitionIds.IndexOf(eventId);
        (runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex],
            runtime.State.WorldEventDeckDefinitionIds[eventIndex]) =
            (runtime.State.WorldEventDeckDefinitionIds[eventIndex],
                runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex]);
        return runtime;
    }

    private static void Complete(
        DuckMatchRuntime runtime,
        string playerId,
        int position,
        int flowers = 0,
        int flock = 0,
        DuckGloriousSunshineBenefit benefit = DuckGloriousSunshineBenefit.FreshAir,
        string finalDefinitionId = "seeds",
        bool wornOut = false)
    {
        var player = runtime.Player(playerId);
        var chip = player.Inventory.First(item => item.DefinitionId == finalDefinitionId);
        player.BagPhysicalChipIds.Remove(chip.PhysicalChipId);
        player.Position = position;
        player.FlowersPlaced = flowers;
        player.ActiveFlock = flock;
        if (finalDefinitionId != "seeds")
        {
            var seed = player.Inventory.First(item => item.DefinitionId == "seeds");
            player.BagPhysicalChipIds.Remove(seed.PhysicalChipId);
            player.PlacedChips.Add(new DuckPlacedChipState(seed.PhysicalChipId, Math.Max(1, position - 1), nuisanceSuppressed: false));
        }
        player.PlacedChips.Add(new DuckPlacedChipState(chip.PhysicalChipId, position, nuisanceSuppressed: false));
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
        player.HasFinishedDay = true;
        player.IsWornOut = wornOut;
        if (runtime.CurrentEvent.EventType == DuckWorldEventType.GloriousSunshine)
        {
            player.HasGloriousSunshineChoice = true;
            player.GloriousSunshineBenefit = benefit;
            player.SafeExhaustionMaximum = benefit == DuckGloriousSunshineBenefit.FreshAir
                ? runtime.Rules.FreshAirSafeExhaustionMaximum
                : 5;
        }
    }
}
