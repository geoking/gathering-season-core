using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckPersistenceTests
{
    private static readonly JsonSerializerOptions SaveJson = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    [Fact]
    public void Capture_is_a_detached_complete_value_and_restore_exactly_recreates_an_ordinary_action()
    {
        var runtime = DuckMatchRuntime.Create(seed: 101);
        PutDefinitionFirst(runtime.Player("human"), "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        Execute(match, "human", GameActionKind.Explore);

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save);
        AssertEquivalent(save, DuckSaves.Capture(restored));

        save.Players.Single(player => player.Id == "human").TotalTwigs = 999;
        save.WorldEventDeckDefinitionIds.Reverse();
        Assert.NotEqual(999, restored.GetSnapshot("human").Players.Single(player => player.Id == "human").TotalTwigs);

        Execute(match, "human", GameActionKind.Settle);
        Execute(restored, "human", GameActionKind.Settle);
        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
    }

    [Fact]
    public void Signpost_preview_and_ordered_future_draw_continue_without_rerolling()
    {
        var runtime = DuckMatchRuntime.Create(seed: 202);
        var human = runtime.Player("human");
        PutDefinitionFirst(human, "seeds");
        PutDefinitionFirst(human, "signpost");
        var match = new MatchSession<DuckMatchView>(runtime);

        Execute(match, "human", GameActionKind.Explore);
        var preview = Assert.Single(match.GetSnapshot("human").KnownNextChips);
        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save);
        Assert.Equal(preview.PhysicalChipId, Assert.Single(restored.GetSnapshot("human").KnownNextChips).PhysicalChipId);

        Execute(match, "human", GameActionKind.Explore);
        Execute(restored, "human", GameActionKind.Explore);
        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
    }

    [Fact]
    public void Pending_final_Day_commitment_restores_privately_and_reveals_the_same_atomic_beat()
    {
        var match = MatchSession.CreateDuck(seed: 303);
        AdvanceToDay(match, 10);
        var oldHuman = Action(match, "human", GameActionKind.Explore);
        var oldAi = Action(match, "ai", GameActionKind.Explore);
        match.Execute("human", oldHuman);
        Assert.True(match.GetSnapshot("ai").AwaitingFinalDayDecisions);
        Assert.Empty(match.GetLegalActions("human"));

        var save = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(save);
        AssertEquivalent(save, DuckSaves.Capture(restored));
        Assert.True(restored.GetSnapshot("ai").AwaitingFinalDayDecisions);
        Assert.Empty(restored.GetLegalActions("human"));
        Assert.Throws<InvalidOperationException>(() => restored.Execute("ai", oldAi));

        match.Execute("ai", oldAi);
        Execute(restored, "ai", GameActionKind.Explore);
        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
        Assert.False(restored.GetSnapshot("human").AwaitingFinalDayDecisions);
    }

    [Fact]
    public void Purchase_restores_once_and_enters_the_next_bag_without_duplicate_inventory()
    {
        var runtime = DuckMatchRuntime.Create(seed: 404);
        PutDefinitionFirst(runtime.Player("human"), "seeds");
        PutDefinitionFirst(runtime.Player("ai"), "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishAdventureAfterOneDraw(match);
        var oldFinish = Action(match, "human", GameActionKind.FinishDream);
        Execute(match, "human", GameActionKind.BuyEncounter, "seeds");
        var purchasedId = DuckSaves.Capture(match).Players.Single(player => player.Id == "human")
            .Inventory.Max(chip => chip.PhysicalChipId);

        var restored = DuckSaves.Restore(DuckSaves.Capture(match));
        Assert.Throws<InvalidOperationException>(() => restored.Execute("human", oldFinish));
        Execute(match, "human", GameActionKind.FinishDream);
        Execute(restored, "human", GameActionKind.FinishDream);
        Execute(match, "ai", GameActionKind.FinishDream);
        Execute(restored, "ai", GameActionKind.FinishDream);
        Execute(match, "human", GameActionKind.NextDay);
        Execute(restored, "human", GameActionKind.NextDay);

        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
        var player = DuckSaves.Capture(restored).Players.Single(candidate => candidate.Id == "human");
        Assert.Single(player.Inventory.Where(chip => chip.PhysicalChipId == purchasedId));
        Assert.Contains(purchasedId, player.BagPhysicalChipIds);
    }

    [Fact]
    public void Resuming_after_Night_rewards_does_not_apply_rewards_a_second_time()
    {
        var match = MatchSession.CreateDuck(seed: 505);
        FinishAdventureAfterOneDraw(match);
        var atNight = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(atNight);
        AssertEquivalent(atNight, DuckSaves.Capture(restored));

        FinishDreamAndBeginNextDay(match);
        FinishDreamAndBeginNextDay(restored);
        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
        Assert.All(DuckSaves.Capture(restored).Players, player =>
            Assert.Equal(1, player.LastNightOutcome!.Day));
    }

    [Fact]
    public void Day_five_Goose_is_preserved_once_across_restore_and_future_Dawns()
    {
        var match = MatchSession.CreateDuck(seed: 606);
        AdvanceToDay(match, 5);
        var restored = DuckSaves.Restore(DuckSaves.Capture(match));
        Assert.All(DuckSaves.Capture(restored).Players, player =>
            Assert.Single(player.Inventory.Where(chip => chip.DefinitionId == "grumpy_goose")));

        AdvanceToDay(match, 6);
        AdvanceToDay(restored, 6);
        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
        Assert.All(DuckSaves.Capture(restored).Players, player =>
            Assert.Single(player.Inventory.Where(chip => chip.DefinitionId == "grumpy_goose")));
    }

    [Fact]
    public void Final_scoring_round_trips_as_a_frozen_result_with_no_further_actions()
    {
        var match = MatchSession.CreateDuck(seed: 707);
        PlayToFinished(match);
        var finished = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(finished);

        AssertEquivalent(finished, DuckSaves.Capture(restored));
        var result = Assert.IsType<DuckFinalResult>(restored.GetSnapshot("human").FinalResult);
        Assert.Equal(2, result.Standings.Count);
        Assert.NotEmpty(result.WinnerIds);
        Assert.Empty(restored.GetLegalActions("human"));
        Assert.Empty(restored.GetLegalActions("ai"));
    }

    [Fact]
    public void Restored_random_state_produces_the_same_bags_and_final_result_for_the_whole_future()
    {
        var match = MatchSession.CreateDuck(seed: 712);
        AdvanceToDay(match, 3);
        Execute(match, "human", GameActionKind.Explore);
        var restored = DuckSaves.Restore(DuckSaves.Capture(match));

        PlayToFinished(match);
        PlayToFinished(restored);

        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
    }

    [Fact]
    public void Public_DTOs_round_trip_through_a_host_serializer_without_Core_owning_JSON()
    {
        var match = MatchSession.CreateDuck(seed: 808);
        FinishAdventureAfterOneDraw(match);
        var json = JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson);
        var decoded = JsonSerializer.Deserialize<DuckSaveData>(json, SaveJson);
        var restored = DuckSaves.Restore(Assert.IsType<DuckSaveData>(decoded));

        AssertEquivalent(DuckSaves.Capture(match), DuckSaves.Capture(restored));
    }

    [Fact]
    public void New_matches_capture_and_round_trip_with_rules_revision_seven_prices()
    {
        var match = MatchSession.CreateDuck(seed: 809);
        var save = DuckSaves.Capture(match);

        Assert.Equal(1, save.FormatVersion);
        Assert.Equal(DuckRules.CurrentRulesRevision, save.RulesVersion);
        Assert.Equal("gathering-season.duck.v1", save.ProfileId);

        var restored = DuckSaves.Restore(save);

        Assert.Equal(7, DuckSaves.Capture(restored).RulesVersion);
        Assert.Equal(1, restored.GetSnapshot("human").ShopOffers.Single(offer => offer.DefinitionId == "tailwind_2").Price);
        Assert.Equal(4, restored.GetSnapshot("human").ShopOffers.Single(offer => offer.DefinitionId == "reeds_3").Price);
        AssertEquivalent(save, DuckSaves.Capture(restored));
    }

    [Fact]
    public void Revision_six_save_restores_its_exact_meadow_catalogue()
    {
        var runtime = DuckMatchRuntime.Create(seed: 811, rulesRevision: 6);
        var save = DuckSaves.Capture(new MatchSession<DuckMatchView>(runtime));

        var restored = DuckSaves.Restore(save);
        var view = restored.GetSnapshot("human");

        Assert.Equal(6, view.RulesRevision);
        Assert.Equal(new[] { 4, 10, 16, 22, 25, 32, 36, 43 },
            view.Rules.BoardSpaces.Where(space => space.IsShelter).Select(space => space.Space));
        Assert.True(view.Rules.BoardSpaceAt(16).IsShelter);
        Assert.False(view.Rules.BoardSpaceAt(17).IsShelter);
        Assert.False(view.Rules.BoardSpaceAt(21).IsShelter);
        Assert.True(view.Rules.BoardSpaceAt(22).IsShelter);
        Assert.Equal(save.RulesVersion, DuckSaves.Capture(restored).RulesVersion);
    }

    [Fact]
    public void Legacy_mid_Dream_restore_keeps_paid_Sleep_purchases_and_revision_one_prices()
    {
        var atNight = MatchSession.CreateDuck(seed: 810);
        AdvanceToDay(atNight, 4);
        FinishAdventureAfterOneDraw(atNight);
        var legacy = DuckSaves.Capture(atNight);
        legacy.RulesVersion = 1;
        legacy.WorldEventDeckDefinitionIds[legacy.WorldEventDeckDefinitionIds.IndexOf("glorious_sunshine")] =
            "sunlit_signboards";
        foreach (var player in legacy.Players)
        {
            player.HasGloriousSunshineChoice = false;
            if (player.LastNightOutcome != null) player.LastNightOutcome.GloriousSunshineSleep = 0;
        }
        var human = legacy.Players.Single(player => player.Id == "human");
        human.FrozenSleep = 20;
        human.RemainingSleep = 20;
        human.LastNightOutcome!.PrintedSleep += 20 - human.LastNightOutcome.SleepBeforeWear;
        human.LastNightOutcome.SleepBeforeWear = 20;
        human.LastNightOutcome!.FrozenSleep = 20;

        var restored = DuckSaves.Restore(legacy);
        var legacyPrices = restored.GetSnapshot("human").ShopOffers
            .ToDictionary(offer => offer.DefinitionId, offer => offer.SleepPrice);
        Assert.Equal(new Dictionary<string, int>
        {
            ["seeds"] = 3,
            ["tailwind_2"] = 5,
            ["tailwind_4"] = 10,
            ["tailwind_6"] = 15,
            ["signpost"] = 7,
            ["splash"] = 4,
            ["reeds_1"] = 6,
            ["reeds_2"] = 11,
            ["reeds_3"] = 16,
            ["companion"] = 7,
            ["wildflowers"] = 5
        }, legacyPrices);

        var buy = Action(restored, "human", GameActionKind.BuyEncounter, "tailwind_2");
        Assert.Equal(5, buy.Cost);
        restored.Execute("human", buy);
        var midDream = DuckSaves.Capture(restored);
        Assert.Equal(1, midDream.RulesVersion);
        Assert.Equal(15, midDream.Players.Single(player => player.Id == "human").RemainingSleep);
        Assert.Equal(new[] { "tailwind_2" },
            midDream.Players.Single(player => player.Id == "human").PurchasedEncounterDefinitionIds);

        var restoredAgain = DuckSaves.Restore(midDream);
        var recaptured = DuckSaves.Capture(restoredAgain);
        AssertEquivalent(midDream, recaptured);
        Assert.Equal(1, recaptured.RulesVersion);
        Assert.Equal(15, recaptured.Players.Single(player => player.Id == "human").RemainingSleep);
        Assert.Equal(6, restoredAgain.GetLegalActions("human")
            .Single(action => action.DefinitionId == "reeds_1").Cost);
    }

    [Fact]
    public void Restore_rejects_missing_versions_and_inconsistent_authoritative_data()
    {
        AssertInvalid(save => save.FormatVersion = 0);
        AssertInvalid(save => save.ProfileId = string.Empty);
        AssertInvalid(save => save.ProfileId = "unsupported.duck.v1");
        AssertInvalid(save => save.RulesVersion = 0);
        AssertInvalid(save => save.RulesVersion = 8);
        AssertInvalid(save => save.Settings = null!);
        AssertInvalid(save => save.Random.Algorithm = string.Empty);
        AssertInvalid(save => save.CommandRevisions.Clear());
        AssertInvalid(save => save.WorldEventDeckDefinitionIds[0] = save.WorldEventDeckDefinitionIds[1]);
        AssertInvalid(save => save.Players[0].BagPhysicalChipIds[0] = int.MaxValue);
        AssertInvalid(save => save.Players[0].KnownNextPhysicalChipIds.Add(save.Players[0].BagPhysicalChipIds[1]));
        AssertInvalid(save => save.DayFiveGooseAdded = true);
        AssertInvalid(save => save.Players.Reverse());
        AssertInvalid(save =>
        {
            foreach (var player in save.Players) player.HasFinishedDay = true;
        });

        var atNight = MatchSession.CreateDuck(seed: 910);
        FinishAdventureAfterOneDraw(atNight);
        var invalidNight = DuckSaves.Capture(atNight);
        foreach (var player in invalidNight.Players) player.HasFinishedDream = true;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(invalidNight));

        var pending = MatchSession.CreateDuck(seed: 911);
        AdvanceToDay(pending, 10);
        Execute(pending, "human", GameActionKind.Explore);
        var completeCohort = DuckSaves.Capture(pending);
        completeCohort.FinalDayCommits.Add(new DuckFinalDayCommitSaveData
        {
            Beat = completeCohort.FinalDayDecisionBeat,
            PlayerId = "ai",
            ActionKind = GameActionKind.Explore
        });
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(completeCohort));
    }

    private static void AssertInvalid(Action<DuckSaveData> corrupt)
    {
        var save = DuckSaves.Capture(MatchSession.CreateDuck(seed: 909));
        corrupt(save);
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
    }

    private static void PlayToFinished(MatchSession<DuckMatchView> match)
    {
        while (match.GetSnapshot("human").Phase != DuckPhase.Finished)
        {
            var phase = match.GetSnapshot("human").Phase;
            if (phase == DuckPhase.Adventure)
                FinishAdventureAfterOneDraw(match);
            else if (phase == DuckPhase.Night)
            {
                Execute(match, "human", GameActionKind.FinishDream);
                Execute(match, "ai", GameActionKind.FinishDream);
            }
            else if (phase == DuckPhase.DayComplete)
                Execute(match, "human", GameActionKind.NextDay);
            else
                throw new InvalidOperationException("Unexpected phase while completing match: " + phase);
        }
    }

    private static void AdvanceToDay(MatchSession<DuckMatchView> match, int day)
    {
        while (match.GetSnapshot("human").Day < day)
        {
            FinishAdventureAfterOneDraw(match);
            FinishDreamAndBeginNextDay(match);
        }
    }

    private static void FinishAdventureAfterOneDraw(MatchSession<DuckMatchView> match)
    {
        foreach (var playerId in new[] { "human", "ai" })
        {
            var choice = match.GetLegalActions(playerId)
                .FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit);
            if (choice != null) match.Execute(playerId, choice);
        }
        foreach (var playerId in new[] { "human", "ai" })
            if (match.GetLegalActions(playerId).Any(action => action.Kind == GameActionKind.Explore))
                Execute(match, playerId, GameActionKind.Explore);
        while (match.GetSnapshot("human").Phase == DuckPhase.Adventure)
        {
            foreach (var playerId in new[] { "human", "ai" })
            {
                var settle = match.GetLegalActions(playerId).SingleOrDefault(action => action.Kind == GameActionKind.Settle);
                if (settle != null) match.Execute(playerId, settle);
            }
        }
    }

    private static void FinishDreamAndBeginNextDay(MatchSession<DuckMatchView> match)
    {
        Execute(match, "human", GameActionKind.FinishDream);
        Execute(match, "ai", GameActionKind.FinishDream);
        Execute(match, "human", GameActionKind.NextDay);
    }

    private static void PutDefinitionFirst(DuckPlayerState player, string definitionId)
    {
        var physicalId = player.Inventory.First(chip => chip.DefinitionId == definitionId).PhysicalChipId;
        player.BagPhysicalChipIds.Remove(physicalId);
        player.BagPhysicalChipIds.Insert(0, physicalId);
    }

    private static GameAction Action(MatchSession<DuckMatchView> match, string playerId, GameActionKind kind,
        string? definitionId = null)
    {
        return match.GetLegalActions(playerId).Single(action => action.Kind == kind
            && (definitionId == null || action.DefinitionId == definitionId));
    }

    private static void Execute(MatchSession<DuckMatchView> match, string playerId, GameActionKind kind,
        string? definitionId = null)
    {
        match.Execute(playerId, Action(match, playerId, kind, definitionId));
    }

    private static void AssertEquivalent(DuckSaveData expected, DuckSaveData actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected, SaveJson), JsonSerializer.Serialize(actual, SaveJson));
    }
}
