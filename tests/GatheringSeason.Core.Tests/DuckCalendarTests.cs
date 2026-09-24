using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckCalendarTests
{
    private static readonly DuckRuleDefinitions Rules = DuckRules.ForRulesRevision(3);

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 2, 2)]
    [InlineData(7, 3, 3)]
    [InlineData(10, 3, 0)]
    public void Nest_level_and_purchase_limit_cover_calendar_boundaries(
        int day,
        int expectedNestLevel,
        int expectedPurchaseLimit)
    {
        var runtime = DuckMatchRuntime.Create(seed: 19, rulesRevision: 3);
        runtime.State.Day = day;

        var view = runtime.GetSnapshot("human");

        Assert.Equal(expectedNestLevel, DuckDreamHandler.NestLevelForDay(day));
        Assert.Equal(expectedNestLevel, view.NestLevel);
        Assert.Equal(expectedPurchaseLimit, DuckDreamHandler.PurchaseLimitForDay(day));
    }

    [Fact]
    public void Deterministic_policy_completes_all_ten_Days_and_reveals_each_event_once()
    {
        var first = PlayCompleteMatch(seed: 941);
        var repeat = PlayCompleteMatch(seed: 941);

        Assert.Equal(Enumerable.Range(1, 10), first.DayStarts.Select(start => start.Day));
        Assert.Equal(10, first.DayStarts.Select(start => start.EventId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Rules.WorldEvents.Select(item => item.DefinitionId).OrderBy(id => id),
            first.DayStarts.Select(start => start.EventId).OrderBy(id => id));
        Assert.All(first.DayStarts.Where(start => start.Day < 5), start => Assert.Equal(0, start.GooseCount));
        Assert.All(first.DayStarts.Where(start => start.Day >= 5), start => Assert.Equal(1, start.GooseCount));

        Assert.Equal(DuckPhase.Finished, first.View.Phase);
        Assert.Equal(10, first.View.Day);
        Assert.NotNull(first.View.FinalResult);
        Assert.Empty(first.Match.GetLegalActions("human"));
        Assert.Empty(first.Match.GetLegalActions("ai"));
        Assert.All(first.View.Players, player =>
        {
            Assert.True(player.HasFinishedDay);
            Assert.True(player.HasFinishedDream);
            Assert.True(player.IsSleepFrozen);
            Assert.Equal(0, player.RemainingSleep);
            Assert.Equal(1, MatchGooseCount(first.Match, player.Id));
        });

        Assert.Equal(first.DayStarts, repeat.DayStarts);
        Assert.Equal(
            first.View.FinalResult!.Standings.Select(ScoreTuple),
            repeat.View.FinalResult!.Standings.Select(ScoreTuple));
        Assert.Equal(first.View.FinalResult.WinnerIds, repeat.View.FinalResult.WinnerIds);
    }

    [Fact]
    public void Day_five_adds_one_Goose_per_duck_once_and_keeps_it_in_every_later_bag()
    {
        var runtime = DuckMatchRuntime.Create(seed: 55, rulesRevision: 3);
        CompleteDreamForCalendar(runtime, day: 4);
        var nextPhysicalId = runtime.State.NextPhysicalChipId;

        DuckDayPreparation.BeginNextDay(runtime);

        Assert.True(runtime.State.DayFiveGooseAdded);
        Assert.Equal(nextPhysicalId + runtime.State.Players.Count, runtime.State.NextPhysicalChipId);
        foreach (var player in runtime.State.Players)
        {
            var goose = Assert.Single(player.Inventory.Where(chip => chip.DefinitionId == "grumpy_goose"));
            Assert.Contains(goose.PhysicalChipId, player.BagPhysicalChipIds);
        }

        CompleteDreamForCalendar(runtime, day: 5);
        DuckDayPreparation.BeginNextDay(runtime);

        Assert.Equal(6, runtime.State.Day);
        Assert.Equal(nextPhysicalId + runtime.State.Players.Count, runtime.State.NextPhysicalChipId);
        Assert.All(runtime.State.Players, player =>
        {
            var goose = Assert.Single(player.Inventory.Where(chip => chip.DefinitionId == "grumpy_goose"));
            Assert.Contains(goose.PhysicalChipId, player.BagPhysicalChipIds);
        });
    }

    [Fact]
    public void Calendar_preserves_inventory_and_moves_through_all_nest_purchase_tiers()
    {
        var runtime = DuckMatchRuntime.Create(seed: 87, rulesRevision: 3);
        var inventoryCounts = runtime.State.Players.ToDictionary(player => player.Id, player => player.Inventory.Count);

        for (var completedDay = 1; completedDay < DuckMatchSettings.StandardDays; completedDay++)
        {
            CompleteDreamForCalendar(runtime, completedDay);
            DuckDayPreparation.BeginNextDay(runtime);

            var expectedLimit = runtime.State.Day <= 3 ? 1 : runtime.State.Day <= 6 ? 2 : runtime.State.Day <= 9 ? 3 : 0;
            Assert.Equal(expectedLimit, DuckDreamHandler.PurchaseLimitForDay(runtime.State.Day));
            Assert.Equal(runtime.State.Day == 10 ? 1 : 0, runtime.State.FinalDayDecisionBeat);
            Assert.Equal(runtime.State.Day - 1, runtime.State.CurrentEventIndex);
            foreach (var player in runtime.State.Players)
            {
                var expectedInventory = inventoryCounts[player.Id] + (runtime.State.Day >= 5 ? 1 : 0);
                Assert.Equal(expectedInventory, player.Inventory.Count);
                Assert.Equal(expectedInventory, player.BagPhysicalChipIds.Count);
                Assert.Empty(player.PurchasedEncounterDefinitionIds);
                Assert.Empty(player.PurchasedShopTypes);
                Assert.Equal(player.PermanentFeatherTrail + (player.ActiveMostRestedStep ? 1 : 0), player.EffectiveStart);
                Assert.Equal(player.EffectiveStart, player.Position);
            }
        }

        Assert.Equal(10, runtime.State.Day);
        Assert.Equal(9, runtime.State.CurrentEventIndex);
        Assert.Throws<InvalidOperationException>(() => DuckDayPreparation.BeginNextDay(runtime));
    }

    [Fact]
    public void Final_ranking_uses_total_Twigs_before_frozen_Sleep()
    {
        var highTwigs = FinalPlayer("twigs", position: 1, bankedTwigs: 30);
        var highSleep = FinalPlayer("sleep", position: 43, bankedTwigs: 0);
        var state = FinalState(highTwigs, highSleep);

        DuckNightResolver.Resolve(state, Rules);

        Assert.Equal(DuckPhase.Finished, state.Phase);
        var result = Assert.IsType<DuckFinalResult>(state.FinalResult);
        Assert.Equal("twigs", Assert.Single(result.WinnerIds));
        Assert.True(result.Standings.Single(item => item.PlayerId == "twigs").FrozenNightTenSleep
            < result.Standings.Single(item => item.PlayerId == "sleep").FrozenNightTenSleep);
        Assert.Equal(1, result.Standings.Single(item => item.PlayerId == "twigs").Rank);
    }

    [Fact]
    public void Final_Sleep_tiebreak_includes_worn_ducks_without_Most_Rested_filter()
    {
        var safe = FinalPlayer("safe", position: 1);
        var worn = FinalPlayer("worn", position: 42, worn: true);
        var state = FinalState(safe, worn);
        SetBanksForEqualFinalTwigs(state, target: 50);

        DuckNightResolver.Resolve(state, Rules);

        Assert.Equal(50, safe.TotalTwigs);
        Assert.Equal(50, worn.TotalTwigs);
        Assert.False(worn.LastNightOutcome!.IsMostRested);
        Assert.True(worn.FrozenSleep > safe.FrozenSleep);
        Assert.Equal("worn", Assert.Single(state.FinalResult!.WinnerIds));
        Assert.True(state.FinalResult.Standings.Single(item => item.PlayerId == "worn").IsWinner);
    }

    [Fact]
    public void Equal_final_Twigs_and_Sleep_produce_tied_winners_and_complete_breakdowns()
    {
        var first = FinalPlayer("first", position: 4, bankedTwigs: 9);
        var second = FinalPlayer("second", position: 4, bankedTwigs: 9);
        var state = FinalState(first, second);

        DuckNightResolver.Resolve(state, Rules);

        var result = Assert.IsType<DuckFinalResult>(state.FinalResult);
        Assert.Equal(new[] { "first", "second" }, result.WinnerIds.OrderBy(id => id));
        Assert.All(result.Standings, standing =>
        {
            Assert.Equal(1, standing.Rank);
            Assert.True(standing.IsWinner);
            Assert.Equal(standing.TotalTwigs,
                state.Players.Single(player => player.Id == standing.PlayerId).TotalTwigs);
            Assert.Equal(standing.FrozenNightTenSleep,
                state.Players.Single(player => player.Id == standing.PlayerId).LastNightOutcome!.FrozenSleep);
            Assert.Equal(standing.DreamTwigs,
                state.Players.Single(player => player.Id == standing.PlayerId).LastNightOutcome!.DreamTwigs);
        });
    }

    [Fact]
    public void Final_Night_banks_Dream_Twigs_once_and_snapshot_exposes_the_same_result()
    {
        var runtime = DuckMatchRuntime.Create(seed: 123, rulesRevision: 3);
        runtime.State.Day = 10;
        runtime.State.CurrentEventIndex = 0;
        runtime.State.WorldEventDeckDefinitionIds[0] = "rain_softened_seeds";
        PrepareFinalPlayer(runtime.Player("human"), position: 43, physicalChipId: 1, definitionId: "splash");
        PrepareFinalPlayer(runtime.Player("ai"), position: 42, physicalChipId: 14, definitionId: "loose_pebbles", worn: true);
        var expected = DuckNightResolver.Calculate(runtime.State, Rules);
        var before = runtime.State.Players.ToDictionary(player => player.Id, player => player.TotalTwigs);

        DuckNightResolver.Resolve(runtime.State, Rules);

        foreach (var player in runtime.State.Players)
            Assert.Equal(before[player.Id] + expected[player.Id].PrintedTwigs - expected[player.Id].BramblesPenalty
                + expected[player.Id].DreamTwigs, player.TotalTwigs);
        var totals = runtime.State.Players.Select(player => player.TotalTwigs).ToArray();
        Assert.Throws<InvalidOperationException>(() => DuckNightResolver.Resolve(runtime.State, Rules));
        Assert.Equal(totals, runtime.State.Players.Select(player => player.TotalTwigs));

        var view = runtime.GetSnapshot("human");
        Assert.Equal(DuckPhase.Finished, view.Phase);
        Assert.Same(runtime.State.FinalResult, view.FinalResult);
        Assert.NotEmpty(view.FinalResult!.WinnerIds);
        Assert.All(view.Players, player => Assert.Equal(0, player.RemainingSleep));
    }

    private static CompleteMatchResult PlayCompleteMatch(int seed)
    {
        var match = MatchSession.CreateDuck(seed);
        var dayStarts = new List<DayStart>();
        var observedDay = 0;
        for (var step = 0; step < 500; step++)
        {
            var view = match.GetSnapshot("human");
            if (view.Phase == DuckPhase.Finished)
                return new CompleteMatchResult(match, view, dayStarts);

            if (view.Phase == DuckPhase.Adventure && view.Day != observedDay)
            {
                observedDay = view.Day;
                dayStarts.Add(new DayStart(
                    view.Day,
                    view.CurrentEvent.DefinitionId,
                    view.OwnInventory.Count(chip => chip.DefinitionId == "grumpy_goose")));
            }

            switch (view.Phase)
            {
                case DuckPhase.Adventure:
                    foreach (var playerId in new[] { "human", "ai" })
                    {
                        var playerView = match.GetSnapshot(playerId).Players.Single(player => player.Id == playerId);
                        var actions = match.GetLegalActions(playerId);
                        var eventChoice = actions.FirstOrDefault(candidate =>
                            candidate.Kind == GameActionKind.ChooseEventBenefit);
                        if (eventChoice != null)
                        {
                            match.Execute(playerId, eventChoice);
                            continue;
                        }
                        var action = playerView.PlacedChips.Count == 0
                            ? actions.SingleOrDefault(candidate => candidate.Kind == GameActionKind.Explore)
                            : actions.SingleOrDefault(candidate => candidate.Kind == GameActionKind.Settle);
                        if (action != null) match.Execute(playerId, action);
                    }
                    break;
                case DuckPhase.Night:
                    foreach (var playerId in new[] { "human", "ai" })
                    {
                        var action = match.GetLegalActions(playerId)
                            .SingleOrDefault(candidate => candidate.Kind == GameActionKind.FinishDream);
                        if (action != null) match.Execute(playerId, action);
                    }
                    break;
                case DuckPhase.DayComplete:
                    var next = match.GetLegalActions("human").Single(candidate => candidate.Kind == GameActionKind.NextDay);
                    match.Execute("human", next);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected phase " + view.Phase + ".");
            }
        }
        throw new InvalidOperationException("The deterministic policy exceeded its action bound.");
    }

    private static void CompleteDreamForCalendar(DuckMatchRuntime runtime, int day)
    {
        runtime.State.Day = day;
        runtime.State.CurrentEventIndex = day - 1;
        runtime.State.Phase = DuckPhase.DayComplete;
        foreach (var player in runtime.State.Players)
        {
            player.HasFinishedDay = true;
            player.HasFinishedDream = true;
        }
    }

    private static DuckMatchState FinalState(params DuckPlayerState[] players)
    {
        var state = new DuckMatchState(DuckMatchSettings.Standard, rulesRevision: 3)
        {
            Day = 10,
            Phase = DuckPhase.Adventure,
            CurrentEventIndex = 0,
            NextPhysicalChipId = 100
        };
        state.WorldEventDeckDefinitionIds.Add("rain_softened_seeds");
        state.Players.AddRange(players);
        return state;
    }

    private static DuckPlayerState FinalPlayer(string id, int position, int bankedTwigs = 0, bool worn = false)
    {
        var player = new DuckPlayerState(id, id, startingFeathers: 0)
        {
            Position = position,
            TotalTwigs = bankedTwigs,
            HasFinishedDay = true,
            IsWornOut = worn
        };
        player.Inventory.Add(new DuckPhysicalChipState(1, "splash"));
        player.PlacedChips.Add(new DuckPlacedChipState(1, position, nuisanceSuppressed: false));
        return player;
    }

    private static void SetBanksForEqualFinalTwigs(DuckMatchState state, int target)
    {
        var outcomes = DuckNightResolver.Calculate(state, Rules);
        foreach (var player in state.Players)
        {
            var outcome = outcomes[player.Id];
            player.TotalTwigs = target - outcome.PrintedTwigs + outcome.BramblesPenalty - outcome.DreamTwigs;
            Assert.True(player.TotalTwigs >= 0);
        }
    }

    private static void PrepareFinalPlayer(
        DuckPlayerState player,
        int position,
        int physicalChipId,
        string definitionId,
        bool worn = false)
    {
        player.Inventory.Clear();
        player.BagPhysicalChipIds.Clear();
        player.PlacedChips.Clear();
        player.Inventory.Add(new DuckPhysicalChipState(physicalChipId, definitionId));
        player.PlacedChips.Add(new DuckPlacedChipState(physicalChipId, position, nuisanceSuppressed: false));
        player.Position = position;
        player.HasFinishedDay = true;
        player.IsWornOut = worn;
        player.IsSleepFrozen = false;
    }

    private static (string PlayerId, int Rank, int TotalTwigs, int FrozenSleep, int DreamTwigs, bool IsWinner)
        ScoreTuple(DuckFinalStanding standing) =>
        (standing.PlayerId, standing.Rank, standing.TotalTwigs, standing.FrozenNightTenSleep,
            standing.DreamTwigs, standing.IsWinner);

    private static int MatchGooseCount(MatchSession<DuckMatchView> match, string playerId) =>
        match.GetSnapshot(playerId).OwnInventory.Count(chip => chip.DefinitionId == "grumpy_goose");

    private sealed record DayStart(int Day, string EventId, int GooseCount);
    private sealed record CompleteMatchResult(
        MatchSession<DuckMatchView> Match,
        DuckMatchView View,
        IReadOnlyList<DayStart> DayStarts);
}
