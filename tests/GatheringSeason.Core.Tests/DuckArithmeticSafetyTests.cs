using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckArithmeticSafetyTests
{
    private static readonly JsonSerializerOptions SaveJson = new() { IncludeFields = true };
    [Theory]
    [InlineData("Exhaustion")]
    [InlineData("NextPhysicalChipId")]
    [InlineData("TotalTwigs")]
    [InlineData("DayReedsTwigs")]
    [InlineData("DayEventTwigs")]
    [InlineData("PermanentFeatherTrail")]
    [InlineData("FlowersPlaced")]
    [InlineData("ActiveFlock")]
    [InlineData("DawnTwigDeficit")]
    [InlineData("CommandRevision")]
    public void Extreme_save_counters_are_rejected_without_changing_the_input(string counter)
    {
        var save = DuckSaves.Capture(MatchSession.CreateDuck(seed: 42));
        var player = save.Players[0];
        if (counter == "NextPhysicalChipId") save.NextPhysicalChipId = int.MaxValue;
        else if (counter == "CommandRevision") save.CommandRevisions[0].Revision = long.MaxValue - 1;
        else typeof(DuckPlayerSaveData).GetField(counter)!.SetValue(player, int.MaxValue);
        var before = JsonSerializer.Serialize(save, SaveJson);
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Validate(save));
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        Assert.Equal(before, JsonSerializer.Serialize(save, SaveJson));
    }

    [Fact]
    public void Fallen_Log_exhaustion_overflow_is_rejected_before_pouch_or_history_mutation()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var player = runtime.Player("human");
        var log = player.Inventory.First(chip => chip.DefinitionId == "fallen_log").PhysicalChipId;
        player.BagPhysicalChipIds.Remove(log);
        player.BagPhysicalChipIds.Insert(0, log);
        player.Exhaustion = int.MaxValue;
        AssertAtomicRejection(runtime, "human", GameActionKind.Explore);
    }

    [Fact]
    public void Purchase_ID_exhaustion_is_rejected_before_payment_or_inventory_mutation()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishDayAdventure(match);
        runtime.State.NextPhysicalChipId = int.MaxValue;
        AssertAtomicRejection(runtime, "human", GameActionKind.BuyEncounter);
    }

    [Fact]
    public void Command_revision_exhaustion_is_rejected_before_runtime_execution()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var match = MatchSession<DuckMatchView>.RestoreDuck(runtime,
            new Dictionary<string, long> { ["human"] = long.MaxValue - 1, ["ai"] = 0 });
        var action = match.GetLegalActions("human").First();
        var before = Fingerprint(runtime);
        Assert.Throws<InvalidOperationException>(() => match.Execute("human", action));
        Assert.Equal(before, Fingerprint(runtime));
    }

    [Fact]
    public void Final_beat_and_other_cohort_arithmetic_are_checked_before_a_commit_is_added()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        runtime.State.Day = 10;
        runtime.State.CurrentEventIndex = 9;
        runtime.State.FinalDayDecisionBeat = int.MaxValue;
        ChooseWeather(new MatchSession<DuckMatchView>(runtime));
        AssertAtomicRejection(runtime, "human", GameActionKind.Explore);

        runtime.State.FinalDayDecisionBeat = 1;
        runtime.Player("ai").TotalTwigs = int.MaxValue;
        AssertAtomicRejection(runtime, "human", GameActionKind.Explore);
    }

    [Fact]
    public void A_later_final_Day_seat_cannot_partially_resolve_an_existing_commitment()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        runtime.State.Day = 10;
        runtime.State.CurrentEventIndex = 9;
        runtime.State.FinalDayDecisionBeat = 1;
        var match = new MatchSession<DuckMatchView>(runtime);
        ChooseWeather(match);
        match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore));
        Assert.Single(runtime.State.FinalDayCommits);
        runtime.Player("ai").TotalTwigs = int.MaxValue;
        AssertAtomicRejection(runtime, "ai", GameActionKind.Explore);
        Assert.Single(runtime.State.FinalDayCommits);
        Assert.Empty(runtime.Player("human").PlacedChips);
    }

    [Fact]
    public void Day_five_checks_all_Goose_ID_allocations_before_advancing_the_calendar()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        runtime.State.Day = 4;
        runtime.State.CurrentEventIndex = 3;
        runtime.State.Phase = DuckPhase.DayComplete;
        foreach (var player in runtime.State.Players) player.HasFinishedDream = true;
        runtime.State.NextPhysicalChipId = int.MaxValue - 1;
        AssertAtomicRejection(runtime, "human", GameActionKind.NextDay);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Catalogue_reward_helpers_do_not_overflow_extreme_nonnegative_counts(int revision)
    {
        var economy = DuckRules.ForRulesRevision(revision).Economy;
        Assert.Equal(economy.FlowersRewardLimit, economy.CalculateFlowerReward(int.MaxValue));
        Assert.Equal(economy.FlockLeaderRewardLimit, economy.CalculateFlockLeaderReward(int.MaxValue));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 2)]
    [InlineData(7, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 2)]
    [InlineData(8, 3)]
    [InlineData(8, 4)]
    public void Every_retained_catalogue_continues_deterministically_at_each_command_boundary(int revision, int seats)
    {
        var match = new MatchSession<DuckMatchView>(DuckMatchRuntime.Create(73,
            new DuckMatchSettings(seats), revision));
        while (match.GetSnapshot("human").Phase != DuckPhase.Finished)
        {
            var restored = DuckSaves.Restore(DuckSaves.Capture(match));
            var seat = Enumerable.Range(0, seats).Select(DuckPlayerSeats.Id)
                .First(id => match.GetLegalActions(id).Count > 0);
            var actions = match.GetLegalActions(seat);
            var action = actions.OrderByDescending(candidate => candidate.Cost).First();
            match.Execute(seat, action);
            restored.Execute(seat, restored.GetLegalActions(seat).Single(candidate => candidate.Id == action.Id));
            Assert.Equal(JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson),
                JsonSerializer.Serialize(DuckSaves.Capture(restored), SaveJson));
        }
    }

    [Theory]
    [InlineData("DayReedsTwigs", 2147483644)]
    [InlineData("DayEventTwigs", 2147483646)]
    [InlineData("FlowersPlaced", 2147483646)]
    [InlineData("ActiveFlock", 2147483646)]
    [InlineData("TotalTwigs", 2147483647)]
    [InlineData("PermanentFeatherTrail", 2147483643)]
    public void Other_seat_arithmetic_headroom_is_checked_before_any_command_mutates_state(string counter, int value)
    {
        var runtime = DuckMatchRuntime.Create(seed: 42, rulesRevision: 1);
        typeof(DuckPlayerState).GetProperty(counter)!.SetValue(runtime.Player("ai"), value);
        AssertAtomicRejection(runtime, "human", GameActionKind.Explore);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Legacy_Flowers_above_the_current_cap_preserve_their_reward_when_restored(int revision)
    {
        var runtime = DuckMatchRuntime.Create(seed: 42, rulesRevision: revision);
        foreach (var player in runtime.State.Players)
        {
            var log = player.Inventory.First(chip => chip.DefinitionId == "fallen_log").PhysicalChipId;
            player.BagPhysicalChipIds.Remove(log);
            player.PlacedChips.Add(new DuckPlacedChipState(log, 4, false));
            player.Position = 4;
            player.FlowersPlaced = 2;
            player.HasFinishedDay = true;
        }
        DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        var match = new MatchSession<DuckMatchView>(runtime);
        var save = DuckSaves.Capture(match);
        Assert.Equal(4, save.Players[0].LastNightOutcome!.FlowerSleep);
        Assert.Equal(JsonSerializer.Serialize(save, SaveJson),
            JsonSerializer.Serialize(DuckSaves.Capture(DuckSaves.Restore(save)), SaveJson));
    }

    [Fact]
    public void Extreme_Night_breakdown_and_final_beat_are_rejected_without_editing_the_DTO()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishDayAdventure(match);
        var save = DuckSaves.Capture(match);
        var outcome = save.Players[0].LastNightOutcome!;
        outcome.PrintedSleep = int.MaxValue;
        outcome.SleepBeforeWear = int.MaxValue;
        outcome.FrozenSleep = int.MaxValue;
        save.Players[0].FrozenSleep = int.MaxValue;
        save.Players[0].RemainingSleep = int.MaxValue;
        var before = JsonSerializer.Serialize(save, SaveJson);
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        Assert.Equal(before, JsonSerializer.Serialize(save, SaveJson));

        AdvanceToDay(match, 10);
        save = DuckSaves.Capture(match);
        save.FinalDayDecisionBeat = int.MaxValue;
        before = JsonSerializer.Serialize(save, SaveJson);
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
        Assert.Equal(before, JsonSerializer.Serialize(save, SaveJson));
    }

    [Fact]
    public void Conservative_calendar_bounds_accept_the_boundary_and_reject_the_next_value()
    {
        var save = DuckSaves.Capture(MatchSession.CreateDuck(seed: 42));
        var limits = new DuckStateLimits(DuckRules.V1, save.Day);
        save.NextPhysicalChipId = limits.Inventory * save.Players.Count + 1;
        save.CommandRevisions[0].Revision = 0;
        save.Players[0].TotalTwigs = limits.TotalTwigs;
        DuckSaves.Validate(save);
        save.NextPhysicalChipId++;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Validate(save));
        save.NextPhysicalChipId--;
        save.CommandRevisions[0].Revision++;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Validate(save));
    }

    [Theory]
    [InlineData("Exhaustion", "fallen_log")]
    [InlineData("ActiveFlock", "companion")]
    [InlineData("FlowersPlaced", "wildflowers")]
    [InlineData("DayReedsTwigs", "reeds_3")]
    [InlineData("DayEventTwigs", "seeds")]
    [InlineData("TotalTwigs", "seeds")]
    [InlineData("PermanentFeatherTrail", "seeds")]
    public void Accepted_Adventure_capacity_boundaries_reject_growth_without_changing_save_or_revision(
        string counter, string nextDefinition)
    {
        var save = DuckSaves.Capture(MatchSession.CreateDuck(seed: 42));
        var limits = new DuckStateLimits(DuckRules.V1, save.Day);
        var player = save.Players[0];
        var maximum = counter switch
        {
            "Exhaustion" => limits.Exhaustion,
            "ActiveFlock" or "FlowersPlaced" => limits.Inventory,
            "DayReedsTwigs" => limits.ReedsTwigs,
            "DayEventTwigs" => 1,
            "TotalTwigs" => limits.TotalTwigs,
            "PermanentFeatherTrail" => limits.FeatherTrail,
            _ => throw new InvalidOperationException()
        };
        typeof(DuckPlayerSaveData).GetField(counter)!.SetValue(player, maximum);
        if (counter == "DayReedsTwigs" || counter == "DayEventTwigs") player.TotalTwigs = maximum;
        if (counter == "PermanentFeatherTrail") player.Position = player.EffectiveStart = maximum;
        if (counter == "DayEventTwigs")
        {
            var goldenMorning = save.WorldEventDeckDefinitionIds.IndexOf("golden_morning");
            (save.WorldEventDeckDefinitionIds[0], save.WorldEventDeckDefinitionIds[goldenMorning]) =
                (save.WorldEventDeckDefinitionIds[goldenMorning], save.WorldEventDeckDefinitionIds[0]);
        }
        player.Inventory.Single(chip => chip.PhysicalChipId == player.BagPhysicalChipIds[0]).DefinitionId = nextDefinition;
        AssertAcceptedSaveRejectsAtomically(save, "human", GameActionKind.Explore);
    }

    [Theory]
    [InlineData("NextPhysicalChipId")]
    [InlineData("Inventory")]
    public void Accepted_Night_capacity_boundaries_reject_purchase_before_payment_or_revision_change(string counter)
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishDayAdventure(match);
        var save = DuckSaves.Capture(match);
        var limits = new DuckStateLimits(DuckRules.V1, save.Day);
        if (counter == "NextPhysicalChipId") save.NextPhysicalChipId = limits.Inventory * save.Players.Count + 1;
        else
        {
            var player = save.Players[0];
            while (player.Inventory.Count < limits.Inventory)
            {
                var id = save.NextPhysicalChipId++;
                player.Inventory.Add(new DuckPhysicalChipSaveData { PhysicalChipId = id, DefinitionId = "seeds" });
                player.BagPhysicalChipIds.Add(id);
            }
        }
        AssertAcceptedSaveRejectsAtomically(save, "human", GameActionKind.BuyEncounter);
    }

    [Fact]
    public void Accepted_final_beat_capacity_rejects_before_pending_commit_or_revision_change()
    {
        var match = MatchSession.CreateDuck(seed: 42);
        AdvanceToDay(match, 10);
        var save = DuckSaves.Capture(match);
        save.FinalDayDecisionBeat = new DuckStateLimits(DuckRules.V1, save.Day).Inventory + 1;
        AssertAcceptedSaveRejectsAtomically(save, "human", GameActionKind.Explore);
    }

    [Fact]
    public void Command_revision_boundary_advances_with_the_command_it_authorizes()
    {
        var runtime = DuckMatchRuntime.Create(seed: 42);
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishDayAdventure(match);
        var save = DuckSaves.Capture(match);
        var player = save.Players[0];
        save.CommandRevisions[0].Revision = player.PlacedChips.Count + 1;
        match = DuckSaves.Restore(save);
        var revision = save.CommandRevisions[0].Revision;
        match.Execute("human", match.GetLegalActions("human").First(action => action.Kind == GameActionKind.BuyEncounter));
        var continued = DuckSaves.Capture(match);
        Assert.Equal(revision + 1, continued.CommandRevisions[0].Revision);
        match = DuckSaves.Restore(continued);
        match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.FinishDream));
        Assert.Equal(revision + 2, DuckSaves.Capture(match).CommandRevisions[0].Revision);
    }

    [Fact]
    public void Accepted_Dawn_start_beyond_the_board_is_rejected_without_calendar_or_reward_mutation()
    {
        var save = DawnStartFixture(trail: 42);
        AssertAcceptedSaveRejectsAtomically(save, "human", GameActionKind.NextDay);
    }

    [Fact]
    public void Largest_saveable_Dawn_start_continues_and_round_trips_at_the_oasis()
    {
        var match = DuckSaves.Restore(DawnStartFixture(trail: 39));
        match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.NextDay));
        var save = DuckSaves.Capture(match);
        Assert.Equal(10, save.Day);
        Assert.Equal(43, save.Players[0].Position);
        Assert.Equal(42, save.Players[0].PermanentFeatherTrail);
        Assert.Equal(3, save.Players[0].DawnFeathersAwarded);
        Assert.Equal(JsonSerializer.Serialize(save, SaveJson),
            JsonSerializer.Serialize(DuckSaves.Capture(DuckSaves.Restore(save)), SaveJson));
    }

    private static DuckSaveData DawnStartFixture(int trail)
    {
        var match = MatchSession.CreateDuck(seed: 42);
        AdvanceToDay(match, 9);
        FinishDayAdventure(match);
        foreach (var seat in new[] { "human", "ai" })
            match.Execute(seat, match.GetLegalActions(seat).Single(action => action.Kind == GameActionKind.FinishDream));
        var save = DuckSaves.Capture(match);
        var human = save.Players[0];
        human.PermanentFeatherTrail = trail;
        human.PendingMostRestedStep = true;
        human.TotalTwigs = Math.Max(5, human.DayReedsTwigs + human.DayEventTwigs);
        save.Players[1].TotalTwigs = human.TotalTwigs + 11;
        DuckSaves.Validate(save);
        return save;
    }

    private static void AssertAcceptedSaveRejectsAtomically(DuckSaveData save, string seat, GameActionKind kind)
    {
        DuckSaves.Validate(save);
        var match = DuckSaves.Restore(save);
        var before = JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson);
        var action = match.GetLegalActions(seat).First(candidate => candidate.Kind == kind);
        Assert.Throws<InvalidOperationException>(() => match.Execute(seat, action));
        Assert.Equal(before, JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson));
        Assert.Contains(match.GetLegalActions(seat), candidate => candidate.Id == action.Id);
    }

    private static void AdvanceToDay(MatchSession<DuckMatchView> match, int day)
    {
        while (match.GetSnapshot("human").Day < day)
        {
            var seat = new[] { "human", "ai" }.First(id => match.GetLegalActions(id).Count > 0);
            match.Execute(seat, match.GetLegalActions(seat).First());
        }
    }

    private static void AssertAtomicRejection(DuckMatchRuntime runtime, string seat, GameActionKind kind)
    {
        var match = new MatchSession<DuckMatchView>(runtime);
        var action = match.GetLegalActions(seat).First(candidate => candidate.Kind == kind);
        var before = Fingerprint(runtime);
        Assert.Throws<InvalidOperationException>(() => match.Execute(seat, action));
        Assert.Equal(before, Fingerprint(runtime));
    }

    private static string Fingerprint(DuckMatchRuntime runtime) =>
        JsonSerializer.Serialize(runtime.State, SaveJson);

    private static void ChooseWeather(MatchSession<DuckMatchView> match)
    {
        foreach (var id in new[] { "human", "ai" })
        {
            var choice = match.GetLegalActions(id).FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit);
            if (choice != null) match.Execute(id, choice);
        }
    }

    private static void FinishDayAdventure(MatchSession<DuckMatchView> match)
    {
        ChooseWeather(match);
        foreach (var id in new[] { "human", "ai" })
        {
            match.Execute(id, match.GetLegalActions(id).Single(action => action.Kind == GameActionKind.Explore));
            var rest = match.GetLegalActions(id).FirstOrDefault(action => action.Kind == GameActionKind.Settle);
            if (rest != null) match.Execute(id, rest);
        }
    }
}
