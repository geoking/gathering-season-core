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
    public void New_match_exposes_revision_seven_rules_and_unchanged_board_reward_tiers()
    {
        var view = MatchSession.CreateDuck(seed: 4001).GetSnapshot("human");
        var rules = view.Rules;

        Assert.Equal(7, view.RulesRevision);
        Assert.Same(rules.Economy, view.Economy);
        Assert.Equal("Stars", view.Economy.CurrencyName);
        Assert.True(view.Economy.UsesStars);
        Assert.All(rules.BoardSpaces.Where(space => space.Space <= 14 && !space.IsShelter), space => Assert.Equal(1, space.Reward));
        Assert.All(rules.BoardSpaces.Where(space => space.Space <= 14 && space.IsShelter), space => Assert.Equal(2, space.Reward));
        Assert.All(rules.BoardSpaces.Where(space => space.Space is >= 15 and <= 28 && !space.IsShelter), space => Assert.Equal(2, space.Reward));
        Assert.All(rules.BoardSpaces.Where(space => space.Space is >= 15 and <= 28 && space.IsShelter), space => Assert.Equal(3, space.Reward));
        Assert.All(rules.BoardSpaces.Where(space => space.Space is >= 29 and <= 42 && !space.IsShelter), space => Assert.Equal(1, space.Reward));
        Assert.All(rules.BoardSpaces.Where(space => space.Space is >= 29 and <= 42 && space.IsShelter), space => Assert.Equal(4, space.Reward));
        Assert.Equal(5, rules.BoardSpaceAt(43).Reward);
    }

    [Fact]
    public void Revision_four_combines_each_approved_Star_bonus_once()
    {
        var runtime = Runtime(day: 10, eventId: "glorious_sunshine");
        Complete(runtime, "human", position: 16, flowers: 3, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        Complete(runtime, "ai", position: 16, flowers: 1, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);

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
    public void Revision_four_collective_events_award_exactly_one_Star_to_each_eligible_duck(
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
    public void Revision_four_worn_duck_takes_Pebbles_before_halving_and_loses_safe_only_bonuses()
    {
        var runtime = Runtime(day: 10, eventId: "glorious_sunshine");
        Complete(runtime, "human", position: 16, flowers: 3, flock: 4,
            DuckGloriousSunshineBenefit.WarmDreams, finalDefinitionId: "loose_pebbles", wornOut: true);
        Complete(runtime, "ai", position: 1, benefit: DuckGloriousSunshineBenefit.FreshAir);

        var outcome = DuckNightResolver.Calculate(runtime.State, runtime.Rules)["human"];

        Assert.Equal(3, outcome.PrintedReward);
        Assert.Equal(0, outcome.FlowerReward);
        Assert.Equal(0, outcome.FinalShelterReward);
        Assert.Equal(0, outcome.FlockReward);
        Assert.Equal(2, outcome.GloriousSunshineReward);
        Assert.Equal(1, outcome.PebblesPenalty);
        Assert.Equal(4, outcome.RewardBeforeWear);
        Assert.Equal(2, outcome.FrozenReward);
        Assert.False(outcome.IsMostRested);
        Assert.Equal(2, outcome.DreamTwigs);
    }

    [Fact]
    public void Revision_four_Restless_Night_subtracts_one_only_from_a_safe_shelter()
    {
        var runtime = Runtime(day: 1, eventId: "restless_night");
        Complete(runtime, "human", position: 16, flowers: 2);
        Complete(runtime, "ai", position: 16, flowers: 2, wornOut: true);

        var outcomes = DuckNightResolver.Calculate(runtime.State, runtime.Rules);
        var safe = outcomes["human"];
        var worn = outcomes["ai"];

        Assert.Equal(3, safe.PrintedReward);
        Assert.Equal(1, safe.FlowerReward);
        Assert.Equal(1, safe.RestlessNightPenalty);
        Assert.Equal(3, safe.FrozenReward);
        Assert.Equal(3, worn.PrintedReward);
        Assert.Equal(0, worn.FlowerReward);
        Assert.Equal(0, worn.RestlessNightPenalty);
        Assert.Equal(1, worn.FrozenReward);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(29, 1)]
    [InlineData(32, 4)]
    [InlineData(43, 5)]
    public void Revision_four_board_uses_each_approved_Star_reward(int position, int expectedStars)
    {
        Assert.Equal(expectedStars, DuckRules.ForRulesRevision(4).BoardSpaceAt(position).Reward);
    }

    [Theory]
    [InlineData("seeds", 0)]
    [InlineData("tailwind_2", 1)]
    [InlineData("tailwind_4", 2)]
    [InlineData("tailwind_6", 3)]
    [InlineData("signpost", 2)]
    [InlineData("splash", 1)]
    [InlineData("reeds_1", 2)]
    [InlineData("reeds_2", 3)]
    [InlineData("reeds_3", 4)]
    [InlineData("companion", 2)]
    [InlineData("wildflowers", 1)]
    public void Revision_four_shop_uses_each_approved_Star_price(string definitionId, int expectedStars)
    {
        Assert.Equal(expectedStars, DuckRules.ForRulesRevision(4).ShopOffer(definitionId).Price);
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
    public void Revision_four_save_round_trip_keeps_Stars_rules_and_zero_price_purchase()
    {
        var runtime = ResolvedDayOneSunshine();
        var match = new MatchSession<DuckMatchView>(runtime);
        var seed = match.GetLegalActions("human").Single(action => action.DefinitionId == "seeds");
        match.Execute("human", seed);

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save);
        var view = restored.GetSnapshot("human");

        Assert.Equal(4, save.RulesVersion);
        Assert.Equal(4, view.RulesRevision);
        Assert.True(view.Economy.UsesStars);
        Assert.Equal(view.Players.Single(player => player.Id == "human").FrozenReward,
            view.Players.Single(player => player.Id == "human").RemainingReward);
        Assert.Contains("seeds", view.Players.Single(player => player.Id == "human").PurchasedEncounterDefinitionIds);
        Assert.DoesNotContain(restored.GetLegalActions("human"), action => action.DefinitionId == "seeds");
    }

    [Fact]
    public void Revision_five_resolved_worn_shelter_rewards_round_trip_without_reinterpretation()
    {
        var runtime = Runtime(day: 10, eventId: "restless_night", rulesRevision: 5);
        Complete(runtime, "human", position: 16, flowers: 2, flock: 2, wornOut: true);
        Complete(runtime, "ai", position: 5, flock: 1);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        var match = new MatchSession<DuckMatchView>(runtime);

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save).GetSnapshot("human");
        var player = restored.Players.Single(candidate => candidate.Id == "human");
        var outcome = Assert.IsType<DuckNightOutcome>(player.LastNightOutcome);

        Assert.Equal(5, save.RulesVersion);
        Assert.Equal(1, player.PermanentFeatherTrail);
        Assert.Equal(1, outcome.FlowerReward);
        Assert.Equal(1, outcome.FinalShelterReward);
        Assert.Equal(1, outcome.RestlessNightPenalty);
        Assert.Equal(1, outcome.FlockReward);
        Assert.Equal(2, outcome.FrozenReward);
        Assert.False(outcome.IsMostRested);
    }

    [Fact]
    public void Save_validation_uses_revision_four_bonus_conversion_and_purchase_parameters()
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

    [Fact]
    public void Normal_uses_the_observed_revision_for_prices_and_currency()
    {
        var policy = new DuckNormalPolicy();
        var stars = DreamRuntime(rulesRevision: 4, reward: 0);
        var sleep = DreamRuntime(rulesRevision: 3, reward: 3);

        var starsMatch = new MatchSession<DuckMatchView>(stars);
        var sleepMatch = new MatchSession<DuckMatchView>(sleep);
        var starsChoice = policy.Choose(starsMatch.GetSnapshot("human"), starsMatch.GetLegalActions("human"));
        var sleepChoice = policy.Choose(sleepMatch.GetSnapshot("human"), sleepMatch.GetLegalActions("human"));

        Assert.Equal("seeds", starsChoice.DefinitionId);
        Assert.Equal(0, starsChoice.Cost);
        Assert.Contains("Stars", policy.Evaluate(starsMatch.GetSnapshot("human"), starsMatch.GetLegalActions("human")).Reason);
        Assert.Equal("seeds", sleepChoice.DefinitionId);
        Assert.Equal(3, sleepChoice.Cost);
        Assert.Contains("Sleep", policy.Evaluate(sleepMatch.GetSnapshot("human"), sleepMatch.GetLegalActions("human")).Reason);
    }

    private static DuckMatchRuntime ResolvedDayOneSunshine()
    {
        var runtime = Runtime(day: 1, eventId: "glorious_sunshine");
        Complete(runtime, "human", position: 4, flowers: 2, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        Complete(runtime, "ai", position: 4, flowers: 1, flock: 2, DuckGloriousSunshineBenefit.WarmDreams);
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        return runtime;
    }

    private static DuckMatchRuntime DreamRuntime(int rulesRevision, int reward)
    {
        var runtime = DuckMatchRuntime.Create(seed: 4003 + rulesRevision, rulesRevision: rulesRevision);
        runtime.State.Phase = DuckPhase.Night;
        foreach (var player in runtime.State.Players)
        {
            player.FrozenSleep = reward;
            player.RemainingSleep = reward;
            player.IsSleepFrozen = true;
            player.HasFinishedDream = false;
        }
        return runtime;
    }

    private static DuckMatchRuntime Runtime(int day, string eventId, int rulesRevision = 4)
    {
        var runtime = DuckMatchRuntime.Create(seed: 4000 + day, rulesRevision: rulesRevision);
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
