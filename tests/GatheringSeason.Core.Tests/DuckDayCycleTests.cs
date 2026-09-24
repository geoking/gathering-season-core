using System;
using System.Linq;
using System.Text.Json;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckDayCycleTests
{
    private static readonly DuckRuleDefinitions Rules = DuckRules.ForRulesRevision(3);
    private static readonly JsonSerializerOptions SaveJson = new JsonSerializerOptions { IncludeFields = true };

    [Fact]
    public void Night_one_offers_the_complete_unlimited_shop_to_each_duck()
    {
        var runtime = DreamRuntime(day: 1, sleep: 100);

        foreach (var player in runtime.State.Players)
        {
            var buys = DuckDreamHandler.GetLegalActions(runtime.State, player, Rules)
                .Where(action => action.Kind == GameActionKind.BuyEncounter)
                .ToArray();
            Assert.Equal(11, buys.Length);
            Assert.Equal(Rules.ShopOffers.Select(offer => offer.DefinitionId).OrderBy(id => id),
                buys.Select(action => action.DefinitionId).OrderBy(id => id));
        }

        var human = runtime.Player("human");
        var ai = runtime.Player("ai");
        DuckDreamHandler.Execute(runtime.State, human, Rules, BuyAction(runtime.State, human, "seeds"));
        DuckDreamHandler.Execute(runtime.State, ai, Rules, BuyAction(runtime.State, ai, "seeds"));

        Assert.Equal("seeds", human.Inventory.Last().DefinitionId);
        Assert.Equal("seeds", ai.Inventory.Last().DefinitionId);
        Assert.NotEqual(human.Inventory.Last().PhysicalChipId, ai.Inventory.Last().PhysicalChipId);
    }

    [Fact]
    public void Dream_shop_enforces_affordability_one_purchase_and_one_variant_per_type()
    {
        var nightOne = DreamRuntime(day: 1, sleep: 7);
        var first = nightOne.Player("human");
        var affordableIds = BuyActions(nightOne.State, first).Select(action => action.DefinitionId).OrderBy(id => id);
        Assert.Equal(new[] { "companion", "seeds", "signpost", "splash", "tailwind_2", "wildflowers" },
            affordableIds);

        DuckDreamHandler.Execute(nightOne.State, first, Rules, BuyAction(nightOne.State, first, "seeds"));
        Assert.Empty(BuyActions(nightOne.State, first));

        var later = DreamRuntime(day: 4, sleep: 100);
        var buyer = later.Player("human");
        DuckDreamHandler.Execute(later.State, buyer, Rules, BuyAction(later.State, buyer, "tailwind_2"));

        Assert.DoesNotContain(BuyActions(later.State, buyer), action =>
            Rules.ShopOffer(action.DefinitionId).ShopType == DuckEncounterType.Tailwind);
        DuckDreamHandler.Execute(later.State, buyer, Rules, BuyAction(later.State, buyer, "seeds"));
        Assert.Empty(BuyActions(later.State, buyer));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 0)]
    public void Purchase_tiers_cover_the_full_calendar(int day, int expectedLimit)
    {
        Assert.Equal(expectedLimit, DuckDreamHandler.PurchaseLimitForDay(day));

        var runtime = DreamRuntime(day, sleep: 100);
        var player = runtime.Player("human");
        for (var purchase = 0; purchase < expectedLimit; purchase++)
        {
            var action = Assert.Single(BuyActions(runtime.State, player)
                .Where(candidate => Rules.ShopOffer(candidate.DefinitionId).ShopType ==
                    new[] { DuckEncounterType.Seeds, DuckEncounterType.Splash, DuckEncounterType.Signpost }[purchase]));
            DuckDreamHandler.Execute(runtime.State, player, Rules, action);
        }

        Assert.Empty(BuyActions(runtime.State, player));
    }

    [Fact]
    public void Purchase_creates_one_physical_chip_in_inventory_but_not_the_current_bag()
    {
        var runtime = DreamRuntime(day: 1, sleep: 10);
        var player = runtime.Player("human");
        var inventoryBefore = player.Inventory.Select(chip => chip.PhysicalChipId).ToArray();
        var bagBefore = player.BagPhysicalChipIds.ToArray();
        var nextPhysicalId = runtime.State.NextPhysicalChipId;

        DuckDreamHandler.Execute(runtime.State, player, Rules, BuyAction(runtime.State, player, "splash"));

        var bought = player.Inventory.Single(chip => chip.PhysicalChipId == nextPhysicalId);
        Assert.Equal("splash", bought.DefinitionId);
        Assert.Equal(inventoryBefore.Length + 1, player.Inventory.Count);
        Assert.Equal(bagBefore, player.BagPhysicalChipIds);
        Assert.DoesNotContain(bought.PhysicalChipId, player.BagPhysicalChipIds);
        Assert.Equal(nextPhysicalId + 1, runtime.State.NextPhysicalChipId);
    }

    [Fact]
    public void Spending_and_finishing_do_not_rewrite_the_frozen_Most_Rested_result()
    {
        var runtime = DreamRuntime(day: 1, sleep: 10);
        var human = runtime.Player("human");
        human.PendingMostRestedStep = true;

        DuckDreamHandler.Execute(runtime.State, human, Rules, BuyAction(runtime.State, human, "tailwind_2"));
        Assert.Equal(10, human.FrozenSleep);
        Assert.Equal(6, human.RemainingSleep);
        Assert.True(human.PendingMostRestedStep);

        DuckDreamHandler.Execute(runtime.State, human, Rules, FinishAction(runtime.State, human));
        Assert.Equal(10, human.FrozenSleep);
        Assert.Equal(0, human.RemainingSleep);
        Assert.True(human.PendingMostRestedStep);
        Assert.True(human.HasFinishedDream);
        Assert.Empty(DuckDreamHandler.GetLegalActions(runtime.State, human, Rules));
        Assert.Equal(DuckPhase.Night, runtime.State.Phase);
        Assert.Throws<InvalidOperationException>(() => DuckDayPreparation.BeginNextDay(runtime));

        var ai = runtime.Player("ai");
        DuckDreamHandler.Execute(runtime.State, ai, Rules, FinishAction(runtime.State, ai));
        Assert.Equal(DuckPhase.DayComplete, runtime.State.Phase);
    }

    [Fact]
    public void Issued_purchase_is_single_use_and_replay_cannot_duplicate_inventory_or_spending()
    {
        var runtime = DreamRuntime(day: 1, sleep: 10);
        var match = new MatchSession<DuckMatchView>(runtime);
        var player = runtime.Player("human");
        var buy = match.GetLegalActions(player.Id).Single(action =>
            action.Kind == GameActionKind.BuyEncounter && action.DefinitionId == "splash");

        match.Execute(player.Id, buy);
        var inventoryAfter = player.Inventory.Select(chip => chip.PhysicalChipId).ToArray();
        var sleepAfter = player.RemainingSleep;

        Assert.Throws<InvalidOperationException>(() => match.Execute(player.Id, buy));
        Assert.Equal(inventoryAfter, player.Inventory.Select(chip => chip.PhysicalChipId));
        Assert.Equal(sleepAfter, player.RemainingSleep);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(int.MaxValue, 3)]
    public void Dawn_delivery_thresholds_are_capped_without_overflow(int deficit, int expectedFeathers)
    {
        Assert.Equal(expectedFeathers, DuckDayPreparation.DawnFeathersForDeficit(deficit));
    }

    [Fact]
    public void Dawn_snapshots_deficits_and_combines_raw_Feather_with_temporary_Most_Rested_step()
    {
        var runtime = CompletedDayOne();
        var leader = runtime.Player("human");
        var trailer = runtime.Player("ai");
        leader.TotalTwigs = 7;
        leader.PermanentFeatherTrail = 2;
        trailer.TotalTwigs = 4;
        trailer.PermanentFeatherTrail = 3;
        trailer.PendingMostRestedStep = true;

        DuckDayPreparation.BeginNextDay(runtime);

        var view = runtime.GetSnapshot("ai");
        var leaderView = view.Players.Single(player => player.Id == leader.Id);
        var trailerView = view.Players.Single(player => player.Id == trailer.Id);
        Assert.Equal((0, 0), (leaderView.DawnTwigDeficit, leaderView.DawnFeathersAwarded));
        Assert.Equal((3, 1), (trailerView.DawnTwigDeficit, trailerView.DawnFeathersAwarded));
        Assert.Equal(4, trailerView.PermanentFeatherTrail);
        Assert.True(trailerView.ActiveMostRestedStep);
        Assert.False(trailer.PendingMostRestedStep);
        Assert.Equal(5, trailerView.EffectiveStart);
        Assert.Equal(5, trailerView.Position);
        var dawnAward = Assert.Single(view.PublicAwards.Where(award => award.DefinitionId == "dawn_delivery"));
        Assert.Equal(2, dawnAward.Day);
        Assert.Equal(new[] { trailer.Id }, dawnAward.PlayerIds);
        Assert.Equal(1, dawnAward.Feathers);
    }

    [Fact]
    public void Seed_42_legally_combines_a_shelter_Feather_and_Most_Rested_across_save_roundtrips()
    {
        var match = MatchSession.CreateDuck(seed: 42);
        var policy = new DuckNormalPolicy();

        for (var step = 0; step < 200 && match.GetSnapshot("human").Phase != DuckPhase.DayComplete; step++)
        {
            var acted = false;
            foreach (var playerId in new[] { "human", "ai" })
            {
                var actions = match.GetLegalActions(playerId);
                if (actions.Count == 0) continue;
                var view = match.GetSnapshot(playerId);
                match.Execute(playerId, policy.Choose(view, actions));
                acted = true;
            }
            Assert.True(acted, "Seed 42 stalled before the first Dawn.");
        }

        var night = match.GetSnapshot("human");
        Assert.Equal(DuckPhase.DayComplete, night.Phase);
        var beforeDawn = night.Players.Single(player => player.Id == "ai");
        Assert.Equal(1, beforeDawn.PermanentFeatherTrail);
        Assert.True(beforeDawn.PendingMostRestedStep);
        Assert.False(beforeDawn.ActiveMostRestedStep);
        Assert.Equal(0, beforeDawn.EffectiveStart);
        match = JsonRoundTrip(match);

        var nextDay = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.NextDay);
        match.Execute("human", nextDay);
        var dawn = match.GetSnapshot("human");
        var ai = dawn.Players.Single(player => player.Id == "ai");
        Assert.Equal(2, dawn.Day);
        Assert.Equal(1, ai.PermanentFeatherTrail);
        Assert.False(ai.PendingMostRestedStep);
        Assert.True(ai.ActiveMostRestedStep);
        Assert.Equal(2, ai.EffectiveStart);
        Assert.Equal(2, ai.Position);

        var restored = JsonRoundTrip(match).GetSnapshot("human").Players.Single(player => player.Id == "ai");
        Assert.Equal((ai.PermanentFeatherTrail, ai.PendingMostRestedStep, ai.ActiveMostRestedStep, ai.EffectiveStart, ai.Position),
            (restored.PermanentFeatherTrail, restored.PendingMostRestedStep, restored.ActiveMostRestedStep, restored.EffectiveStart, restored.Position));
    }

    [Fact]
    public void Dawn_caps_large_deficit_and_resets_transients_without_erasing_reviewable_outcomes()
    {
        var runtime = CompletedDayOne();
        ForceSecondEvent(runtime, "still_air");
        var deckBefore = runtime.State.WorldEventDeckDefinitionIds.ToArray();
        var leader = runtime.Player("human");
        var trailer = runtime.Player("ai");
        leader.TotalTwigs = 11;
        trailer.TotalTwigs = 0;
        var oldOutcome = trailer.LastNightOutcome;
        var bought = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, "wildflowers");
        trailer.Inventory.Add(bought);

        SetTransients(trailer);
        DuckDayPreparation.BeginNextDay(runtime);

        Assert.Equal(2, runtime.State.Day);
        Assert.Equal(DuckPhase.Adventure, runtime.State.Phase);
        Assert.Equal(1, runtime.State.CurrentEventIndex);
        Assert.Equal(deckBefore, runtime.State.WorldEventDeckDefinitionIds);
        Assert.Equal("still_air", runtime.CurrentEvent.DefinitionId);
        Assert.Equal(11, trailer.DawnTwigDeficit);
        Assert.Equal(3, trailer.DawnFeathersAwarded);
        var trailerView = runtime.GetSnapshot(trailer.Id).Players.Single(player => player.Id == trailer.Id);
        Assert.Equal(11, trailerView.DawnTwigDeficit);
        Assert.Equal(3, trailerView.DawnFeathersAwarded);
        Assert.Same(oldOutcome, trailer.LastNightOutcome);
        Assert.Equal(trailer.Inventory.Select(chip => chip.PhysicalChipId).OrderBy(id => id),
            trailer.BagPhysicalChipIds.OrderBy(id => id));
        Assert.Contains(bought.PhysicalChipId, trailer.BagPhysicalChipIds);
        AssertDayTwoTransientsCleared(trailer);
        Assert.Throws<InvalidOperationException>(() => DuckDayPreparation.BeginNextDay(runtime));
        Assert.Equal(3, trailer.DawnFeathersAwarded);
        Assert.Single(runtime.State.PublicAwards.Where(award =>
            award.DefinitionId == "dawn_delivery" && award.PlayerIds.Contains(trailer.Id)));
    }

    [Fact]
    public void Issued_Next_Day_is_single_use_and_cannot_duplicate_Dawn_awards()
    {
        var runtime = CompletedDayOne();
        runtime.Player("human").TotalTwigs = 9;
        runtime.Player("ai").TotalTwigs = 0;
        var match = new MatchSession<DuckMatchView>(runtime);
        var nextDay = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.NextDay);

        match.Execute("human", nextDay);
        var awardsAfter = runtime.State.PublicAwards.ToArray();
        var aiTrailAfter = runtime.Player("ai").PermanentFeatherTrail;

        Assert.Throws<InvalidOperationException>(() => match.Execute("human", nextDay));
        Assert.Equal(awardsAfter, runtime.State.PublicAwards);
        Assert.Equal(aiTrailAfter, runtime.Player("ai").PermanentFeatherTrail);
        Assert.Equal(2, runtime.State.Day);
    }

    [Fact]
    public void Default_match_completes_one_real_Day_purchase_and_Dawn_through_issued_actions()
    {
        var match = MatchSession.CreateDuck(seed: 0);

        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "ai", GameActionKind.Explore);
        Execute(match, "human", GameActionKind.Settle);
        Execute(match, "ai", GameActionKind.Settle);

        var night = match.GetSnapshot("human");
        Assert.Equal(DuckPhase.Night, night.Phase);
        Assert.All(night.Players, player => Assert.NotNull(player.LastNightOutcome));
        var buy = match.GetLegalActions("human").Single(action =>
            action.Kind == GameActionKind.BuyEncounter && action.DefinitionId == "seeds");
        match.Execute("human", buy);
        Execute(match, "ai", GameActionKind.FinishDream);
        Execute(match, "human", GameActionKind.FinishDream);

        Assert.Equal(DuckPhase.DayComplete, match.GetSnapshot("human").Phase);
        Execute(match, "human", GameActionKind.NextDay);

        var dayTwo = match.GetSnapshot("human");
        var human = dayTwo.Players.Single(player => player.Id == "human");
        Assert.Equal(2, dayTwo.Day);
        Assert.Equal(DuckPhase.Adventure, dayTwo.Phase);
        Assert.Equal(14, human.InventoryCount);
        Assert.Equal(14, human.BagCount);
        Assert.Contains(dayTwo.OwnInventory, chip => chip.DefinitionId == "seeds" &&
            !night.OwnInventory.Any(oldChip => oldChip.PhysicalChipId == chip.PhysicalChipId));
        Assert.NotNull(human.LastNightOutcome);
        Assert.Equal(1, human.LastNightOutcome!.Day);
        Assert.False(human.HasFinishedDream);
        Assert.Empty(human.PurchasedEncounterDefinitionIds);
    }

    [Fact]
    public void Night_flock_bonus_awards_one_for_a_solo_companion_and_caps_large_flocks_at_two()
    {
        var solo = ScoringPlayer("solo", position: 5, flock: 1);
        var large = ScoringPlayer("large", position: 6, flock: 10);
        var noFlock = ScoringPlayer("none", position: 5, flock: 0);

        var soloOutcomes = DuckNightResolver.Calculate(ScoringState("rain_softened_seeds", solo, noFlock), Rules);
        var largeOutcomes = DuckNightResolver.Calculate(ScoringState("rain_softened_seeds", large, noFlock), Rules);

        Assert.Equal(1, soloOutcomes[solo.Id].FlockSleep);
        Assert.Equal(0, soloOutcomes[noFlock.Id].FlockSleep);
        Assert.Equal(2, largeOutcomes[large.Id].FlockSleep);
        Assert.Equal(0, largeOutcomes[noFlock.Id].FlockSleep);
    }

    [Fact]
    public void Night_collective_and_Brambles_edges_include_worn_ducks_without_negative_or_duplicate_Twigs()
    {
        var safeShelter = ScoringPlayer("safe", position: 4);
        var wornShelter = ScoringPlayer("worn", position: 10, worn: true);
        var tucked = DuckNightResolver.Calculate(ScoringState("all_tucked_in", safeShelter, wornShelter), Rules);
        Assert.All(tucked.Values, outcome => Assert.Equal(0, outcome.CollectiveEventSleep));

        var firstSupperDuck = ScoringPlayer("supper-one", position: 4, worn: true, finalDefinitionId: "seeds");
        var secondSupperDuck = ScoringPlayer("supper-two", position: 10, worn: true, finalDefinitionId: "seeds");
        firstSupperDuck.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
        secondSupperDuck.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
        var supper = DuckNightResolver.Calculate(
            ScoringState("shared_supper", firstSupperDuck, secondSupperDuck), Rules);
        Assert.All(supper.Values, outcome => Assert.Equal(1, outcome.CollectiveEventSleep));
        Assert.Equal(3, supper[firstSupperDuck.Id].FrozenSleep);
        Assert.Equal(5, supper[secondSupperDuck.Id].FrozenSleep);

        var zero = ScoringPlayer("zero", position: 4, finalDefinitionId: "brambles");
        var retained = ScoringPlayer("retained", position: 4, finalDefinitionId: "brambles", worn: true);
        retained.DayReedsTwigs = 2;
        retained.DayEventTwigs = 1;
        retained.TotalTwigs = 3;
        var state = ScoringState("rain_softened_seeds", zero, retained);
        DuckNightResolver.Resolve(state, Rules);

        Assert.Equal(0, zero.TotalTwigs);
        Assert.Equal(1, zero.LastNightOutcome!.BramblesPenalty);
        Assert.Equal(3, retained.TotalTwigs);
        Assert.Equal(1, retained.LastNightOutcome!.BramblesPenalty);
        Assert.Equal(3, retained.LastNightOutcome.TotalTwigsEarned);
    }

    private static DuckMatchRuntime DreamRuntime(int day, int sleep)
    {
        var runtime = DuckMatchRuntime.Create(seed: 17, rulesRevision: 3);
        runtime.State.Day = day;
        runtime.State.Phase = DuckPhase.Night;
        foreach (var player in runtime.State.Players)
        {
            player.FrozenSleep = sleep;
            player.RemainingSleep = sleep;
            player.IsSleepFrozen = true;
            player.HasFinishedDream = false;
        }
        return runtime;
    }

    private static MatchSession<DuckMatchView> JsonRoundTrip(MatchSession<DuckMatchView> match)
    {
        var json = JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson);
        return DuckSaves.Restore(JsonSerializer.Deserialize<DuckSaveData>(json, SaveJson)!);
    }

    private static DuckMatchState ScoringState(string eventDefinitionId, params DuckPlayerState[] players)
    {
        var state = new DuckMatchState(DuckMatchSettings.Standard, rulesRevision: 3)
        {
            Day = 1,
            Phase = DuckPhase.Adventure,
            CurrentEventIndex = 0,
            NextPhysicalChipId = 100
        };
        state.WorldEventDeckDefinitionIds.Add(eventDefinitionId);
        state.Players.AddRange(players);
        return state;
    }

    private static DuckPlayerState ScoringPlayer(
        string id,
        int position,
        int flock = 0,
        bool worn = false,
        string finalDefinitionId = "splash")
    {
        var player = new DuckPlayerState(id, id, startingFeathers: 0)
        {
            Position = position,
            ActiveFlock = flock,
            IsWornOut = worn,
            HasFinishedDay = true
        };
        player.Inventory.Add(new DuckPhysicalChipState(1, finalDefinitionId));
        player.PlacedChips.Add(new DuckPlacedChipState(1, position, nuisanceSuppressed: false));
        return player;
    }

    private static DuckMatchRuntime CompletedDayOne()
    {
        var runtime = DreamRuntime(day: 1, sleep: 8);
        runtime.State.Phase = DuckPhase.DayComplete;
        foreach (var player in runtime.State.Players)
        {
            player.HasFinishedDay = true;
            player.HasFinishedDream = true;
            player.LastNightOutcome = Outcome();
        }
        return runtime;
    }

    private static void SetTransients(DuckPlayerState player)
    {
        var placedId = player.Inventory[0].PhysicalChipId;
        player.DayReedsTwigs = 2;
        player.DayEventTwigs = 1;
        player.Exhaustion = 4;
        player.SafeExhaustionMaximum = 4;
        player.ActiveFlock = 3;
        player.SplashProtectionArmed = true;
        player.LogSlowdownPending = true;
        player.GuideProtectionAvailable = true;
        player.PocketDriftwoodAwarded = true;
        player.FlowersPlaced = 2;
        player.FrozenSleep = 8;
        player.RemainingSleep = 3;
        player.IsSleepFrozen = true;
        player.HasFinishedDay = true;
        player.HasFinishedDream = true;
        player.IsWornOut = true;
        player.ActiveMostRestedStep = true;
        player.PendingMostRestedStep = false;
        player.PlacedChips.Add(new DuckPlacedChipState(placedId, 4, nuisanceSuppressed: true));
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Splash);
        player.PurchasedEncounterDefinitionIds.Add("wildflowers");
        player.PurchasedShopTypes.Add(DuckEncounterType.Wildflowers);
        player.KnownNextPhysicalChipIds.Add(placedId);
    }

    private static void AssertDayTwoTransientsCleared(DuckPlayerState player)
    {
        Assert.Equal(0, player.DayReedsTwigs);
        Assert.Equal(0, player.DayEventTwigs);
        Assert.Equal(0, player.Exhaustion);
        Assert.Equal(5, player.SafeExhaustionMaximum);
        Assert.Equal(0, player.ActiveFlock);
        Assert.False(player.SplashProtectionArmed);
        Assert.False(player.LogSlowdownPending);
        Assert.False(player.GuideProtectionAvailable);
        Assert.False(player.PocketDriftwoodAwarded);
        Assert.Equal(0, player.FlowersPlaced);
        Assert.Equal(0, player.FrozenSleep);
        Assert.Equal(0, player.RemainingSleep);
        Assert.False(player.IsSleepFrozen);
        Assert.False(player.HasFinishedDay);
        Assert.False(player.HasFinishedDream);
        Assert.False(player.IsWornOut);
        Assert.False(player.ActiveMostRestedStep);
        Assert.Empty(player.PlacedChips);
        Assert.Empty(player.PlacedHelpfulTypes);
        Assert.Empty(player.PurchasedEncounterDefinitionIds);
        Assert.Empty(player.PurchasedShopTypes);
        Assert.Empty(player.KnownNextPhysicalChipIds);
    }

    private static void ForceSecondEvent(DuckMatchRuntime runtime, string definitionId)
    {
        var oldIndex = runtime.State.WorldEventDeckDefinitionIds.IndexOf(definitionId);
        (runtime.State.WorldEventDeckDefinitionIds[1], runtime.State.WorldEventDeckDefinitionIds[oldIndex]) =
            (runtime.State.WorldEventDeckDefinitionIds[oldIndex], runtime.State.WorldEventDeckDefinitionIds[1]);
    }

    private static GameAction[] BuyActions(DuckMatchState state, DuckPlayerState player) =>
        DuckDreamHandler.GetLegalActions(state, player, Rules)
            .Where(action => action.Kind == GameActionKind.BuyEncounter)
            .ToArray();

    private static GameAction BuyAction(DuckMatchState state, DuckPlayerState player, string definitionId) =>
        BuyActions(state, player).Single(action => action.DefinitionId == definitionId);

    private static GameAction FinishAction(DuckMatchState state, DuckPlayerState player) =>
        DuckDreamHandler.GetLegalActions(state, player, Rules)
            .Single(action => action.Kind == GameActionKind.FinishDream);

    private static DuckNightOutcome Outcome() => new DuckNightOutcome(
        day: 1, printedSleep: 3, printedTwigs: 1, reedsTwigs: 0, eventTwigs: 0,
        bramblesPenalty: 0, flowerSleep: 0, finalShelterSleep: 0, restlessNightPenalty: 0,
        collectiveEventSleep: 0, gloriousSunshineSleep: 0, flockSleep: 0, pebblesPenalty: 0, sleepBeforeWear: 3,
        frozenSleep: 3, totalTwigsEarned: 1, feathersAwarded: 0, isMostRested: true,
        nextDayTemporaryStep: 1, dreamTwigs: 0);

    private static void Execute(MatchSession<DuckMatchView> match, string playerId, GameActionKind kind)
    {
        var action = match.GetLegalActions(playerId).Single(candidate => candidate.Kind == kind);
        match.Execute(playerId, action);
    }
}
