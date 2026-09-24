using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckAdventureTests
{
    [Theory]
    [InlineData("seeds", 1, 0, 0)]
    [InlineData("tailwind_2", 2, 0, 0)]
    [InlineData("tailwind_4", 4, 0, 0)]
    [InlineData("tailwind_6", 6, 0, 0)]
    [InlineData("signpost", 2, 0, 0)]
    [InlineData("splash", 1, 0, 0)]
    [InlineData("reeds_1", 1, 1, 0)]
    [InlineData("reeds_2", 1, 2, 0)]
    [InlineData("reeds_3", 1, 3, 0)]
    [InlineData("companion", 2, 0, 0)]
    [InlineData("wildflowers", 1, 0, 0)]
    [InlineData("fallen_log", 1, 0, 1)]
    [InlineData("mud_puddle", 1, 0, 1)]
    [InlineData("loose_pebbles", 1, 0, 1)]
    [InlineData("brambles", 1, 0, 1)]
    [InlineData("grumpy_goose", 1, 0, 1)]
    public void Every_encounter_variant_resolves_its_own_movement_yield_and_Exhaustion(
        string definitionId,
        int expectedMovement,
        int expectedTwigYield,
        int expectedExhaustion)
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", definitionId, "seeds");

        Execute(Session(runtime), "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(expectedMovement, player.Position);
        Assert.Equal(expectedTwigYield, player.DayReedsTwigs);
        Assert.Equal(expectedTwigYield, player.TotalTwigs);
        Assert.Equal(expectedExhaustion, player.Exhaustion);
        Assert.Single(player.PlacedChips);
    }

    [Fact]
    public void First_draw_is_mandatory_and_ordinary_Day_actions_resolve_independently()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "seeds", "tailwind_2");
        SetBag(runtime, "ai", "tailwind_2", "seeds");
        var match = Session(runtime);
        var before = match.GetSnapshot("human");
        var humanExplore = Assert.Single(match.GetLegalActions("human"));
        var aiExplore = Assert.Single(match.GetLegalActions("ai"));

        match.Execute("human", humanExplore);

        Assert.Equal(1, runtime.Player("human").Position);
        Assert.Equal(0, runtime.Player("ai").Position);
        Assert.Empty(before.Players.Single(player => player.Id == "human").PlacedChips);
        Assert.Equal(2, before.OwnBag.Count);
        Assert.Contains(match.GetLegalActions("human"), action => action.Kind == GameActionKind.Settle);

        match.Execute("ai", aiExplore);

        Assert.Equal(2, runtime.Player("ai").Position);
        Assert.Equal(new[] { "human", "ai" }, runtime.State.History.Select(entry => entry.ActorId));
    }

    [Fact]
    public void Issued_commands_reject_cross_player_and_repeated_use()
    {
        var match = MatchSession.CreateDuck(5);
        var action = Assert.Single(match.GetLegalActions("human"));

        Assert.Throws<InvalidOperationException>(() => match.Execute("ai", action));
        match.Execute("human", action);
        Assert.Throws<InvalidOperationException>(() => match.Execute("human", action));
    }

    [Theory]
    [InlineData(DuckWorldEventType.HomeBeforeDark, 1)]
    [InlineData(DuckWorldEventType.SunlitSignboards, 2)]
    [InlineData(DuckWorldEventType.ThickMorningMist, 0)]
    public void Signpost_preview_count_obeys_the_current_event_and_stays_private(
        DuckWorldEventType eventType,
        int expectedPreviewCount)
    {
        var runtime = RuntimeWithEvent(eventType);
        var ids = SetBag(runtime, "human", "signpost", "seeds", "seeds", "splash");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        var humanView = match.GetSnapshot("human");
        var aiView = match.GetSnapshot("ai");

        Assert.Equal(ids.Skip(1).Take(expectedPreviewCount),
            humanView.KnownNextChips.Select(chip => chip.PhysicalChipId));
        Assert.Empty(aiView.KnownNextChips);
        Assert.Equal(humanView.History.Select(entry => entry.Message), aiView.History.Select(entry => entry.Message));
        Assert.Equal(
            humanView.Players.Single(player => player.Id == "human").PlacedChips.Select(chip => (chip.DefinitionId, chip.Position)),
            aiView.Players.Single(player => player.Id == "human").PlacedChips.Select(chip => (chip.DefinitionId, chip.Position)));
        Assert.Null(typeof(DuckPlayerView).GetProperty("KnownNextChips"));
        Assert.DoesNotContain(runtime.State.History, entry => entry.Message.Contains("preview", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sunlit_preview_keeps_the_known_second_chip_when_the_first_is_another_Signpost()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.SunlitSignboards);
        var ids = SetBag(runtime, "human", "signpost", "signpost", "seeds", "splash");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Assert.Equal(new[] { ids[1], ids[2] }, runtime.Player("human").KnownNextPhysicalChipIds);

        Execute(match, "human", GameActionKind.Explore);

        Assert.Equal(new[] { ids[0], ids[1] }, runtime.Player("human").PlacedChips.Select(chip => chip.PhysicalChipId));
        Assert.Equal(new[] { ids[2], ids[3] }, runtime.Player("human").KnownNextPhysicalChipIds);
        Execute(match, "human", GameActionKind.Explore);
        Assert.Equal(ids[2], runtime.Player("human").PlacedChips.Last().PhysicalChipId);
        Assert.Equal(new[] { ids[3] }, runtime.Player("human").KnownNextPhysicalChipIds);
    }

    [Fact]
    public void Rain_is_added_before_Log_halves_Seed_movement()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.RainSoftenedSeeds);
        SetBag(runtime, "human", "fallen_log", "seeds", "seeds");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(new[] { 1, 2 }, player.PlacedChips.Select(chip => chip.Position));
        Assert.False(player.LogSlowdownPending);
    }

    [Fact]
    public void Still_Air_and_pending_Log_halve_Tailwind_only_once()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.StillAir);
        SetBag(runtime, "human", "fallen_log", "tailwind_6", "seeds");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(new[] { 1, 4 }, player.PlacedChips.Select(chip => chip.Position));
        Assert.False(player.LogSlowdownPending);
    }

    [Fact]
    public void Repeated_Logs_do_not_stack_and_a_protected_Log_does_not_clear_an_older_one()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "fallen_log", "fallen_log", "tailwind_4", "seeds");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(new[] { 1, 2, 4 }, player.PlacedChips.Select(chip => chip.Position));
        Assert.False(player.LogSlowdownPending);

        SetBag(runtime, "human", "fallen_log", "seeds");
        player.LogSlowdownPending = true;
        player.SplashProtectionArmed = true;
        Execute(match, "human", GameActionKind.Explore);
        Assert.True(player.PlacedChips.Single().NuisanceSuppressed);
        Assert.True(player.LogSlowdownPending);
        Execute(match, "human", GameActionKind.Explore);
        Assert.False(player.LogSlowdownPending);
    }

    [Fact]
    public void Companion_movement_uses_the_active_flock_and_Mud_never_removes_owned_chips()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "companion", "companion", "mud_puddle", "companion", "mud_puddle", "mud_puddle", "companion", "seeds");
        var match = Session(runtime);

        for (var index = 0; index < 7; index++) Execute(match, "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(new[] { 2, 5, 6, 9, 10, 11, 13 }, player.PlacedChips.Select(chip => chip.Position));
        Assert.Equal(1, player.ActiveFlock);
        Assert.Equal(8, player.Inventory.Count);
        Assert.Contains(DuckEncounterType.Companion, player.PlacedHelpfulTypes);
    }

    [Fact]
    public void Reeds_bank_Twigs_immediately_and_Pocket_Driftwood_awards_once_at_three_helpful_types()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.PocketOfDriftwood);
        SetBag(runtime, "human", "reeds_3", "wildflowers", "seeds", "reeds_1", "splash");
        var match = Session(runtime);

        for (var index = 0; index < 4; index++) Execute(match, "human", GameActionKind.Explore);

        var player = runtime.Player("human");
        Assert.Equal(4, player.DayReedsTwigs);
        Assert.Equal(1, player.DayEventTwigs);
        Assert.Equal(5, player.TotalTwigs);
        Assert.True(player.PocketDriftwoodAwarded);
        Assert.Equal(1, player.FlowersPlaced);
        Assert.Equal(3, player.PlacedHelpfulTypes.Count);
        Assert.Single(runtime.State.History, entry => entry.Message.Contains("Pocket of Driftwood", StringComparison.Ordinal));
    }

    [Fact]
    public void Splash_expires_on_a_helpful_chip_and_consecutive_Splashes_arm_fresh_protection()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "splash", "seeds", "mud_puddle", "splash", "splash", "grumpy_goose", "seeds");
        var player = runtime.Player("human");
        player.ActiveFlock = 1;
        var match = Session(runtime);

        for (var index = 0; index < 6; index++) Execute(match, "human", GameActionKind.Explore);

        Assert.Equal(0, player.ActiveFlock);
        Assert.False(player.PlacedChips[2].NuisanceSuppressed);
        Assert.True(player.PlacedChips[5].NuisanceSuppressed);
        Assert.Equal(5, player.SafeExhaustionMaximum);
        Assert.Equal(2, player.Exhaustion);
        Assert.False(player.SplashProtectionArmed);
    }

    [Fact]
    public void Guide_and_Splash_are_both_consumed_on_the_same_first_obstacle()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.FriendlyGuide);
        SetBag(runtime, "human", "mud_puddle", "fallen_log", "seeds");
        var player = runtime.Player("human");
        player.ActiveFlock = 1;
        player.SplashProtectionArmed = true;
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);

        Assert.Equal(1, player.ActiveFlock);
        Assert.True(player.PlacedChips[0].NuisanceSuppressed);
        Assert.False(player.GuideProtectionAvailable);
        Assert.False(player.SplashProtectionArmed);

        Execute(match, "human", GameActionKind.Explore);
        Assert.False(player.PlacedChips[1].NuisanceSuppressed);
        Assert.True(player.LogSlowdownPending);
    }

    [Fact]
    public void Goose_checks_its_lower_limit_after_always_adding_Exhaustion()
    {
        var unprotected = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(unprotected, "human", "grumpy_goose", "seeds");
        unprotected.Player("human").Exhaustion = 4;
        Execute(Session(unprotected), "human", GameActionKind.Explore);

        Assert.Equal(5, unprotected.Player("human").Exhaustion);
        Assert.Equal(4, unprotected.Player("human").SafeExhaustionMaximum);
        Assert.True(unprotected.Player("human").IsWornOut);
        Assert.True(unprotected.Player("human").HasFinishedDay);

        var protectedRuntime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(protectedRuntime, "human", "grumpy_goose");
        var protectedPlayer = protectedRuntime.Player("human");
        protectedPlayer.Exhaustion = 4;
        protectedPlayer.SplashProtectionArmed = true;
        Execute(Session(protectedRuntime), "human", GameActionKind.Explore);

        Assert.Equal(5, protectedPlayer.Exhaustion);
        Assert.Equal(5, protectedPlayer.SafeExhaustionMaximum);
        Assert.False(protectedPlayer.IsWornOut);
        Assert.True(protectedPlayer.HasFinishedDay);
        Assert.True(protectedPlayer.PlacedChips.Single().NuisanceSuppressed);
    }

    [Theory]
    [InlineData("loose_pebbles")]
    [InlineData("brambles")]
    public void Protection_is_stored_on_the_specific_final_penalty_placement(string definitionId)
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", definitionId, "seeds");
        var player = runtime.Player("human");
        player.SplashProtectionArmed = true;

        Execute(Session(runtime), "human", GameActionKind.Explore);

        Assert.True(player.PlacedChips.Single().NuisanceSuppressed);
        Assert.False(player.SplashProtectionArmed);
    }

    [Fact]
    public void Endpoint_clamps_once_and_fully_resolves_the_lethal_obstacle()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "brambles", "seeds");
        var player = runtime.Player("human");
        player.Position = 42;
        player.Exhaustion = 5;

        Execute(Session(runtime), "human", GameActionKind.Explore);

        Assert.Equal(43, player.Position);
        Assert.Equal(6, player.Exhaustion);
        Assert.True(player.IsWornOut);
        Assert.True(player.HasFinishedDay);
        Assert.Single(player.PlacedChips);
        Assert.Equal(43, player.PlacedChips.Single().Position);
        Assert.Empty(Session(runtime).GetLegalActions("human"));
    }

    [Fact]
    public void Endpoint_overshoot_clamps_each_full_chip_then_scores_the_occupied_endpoint()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "companion", "seeds");
        SetBag(runtime, "ai", "tailwind_6", "seeds");
        var human = runtime.Player("human");
        var ai = runtime.Player("ai");
        human.Position = 40;
        human.ActiveFlock = 3;
        ai.Position = 40;
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Assert.Equal(43, human.Position);
        Assert.Equal(4, human.ActiveFlock);
        Assert.Equal(0, human.Exhaustion);
        Assert.True(human.HasFinishedDay);
        Assert.Equal(43, human.PlacedChips.Single().Position);
        Assert.Single(human.BagPhysicalChipIds);
        Assert.Equal(DuckPhase.Adventure, runtime.State.Phase);

        Execute(match, "ai", GameActionKind.Explore);

        Assert.Equal(43, ai.Position);
        Assert.Equal(0, ai.Exhaustion);
        Assert.True(ai.HasFinishedDay);
        Assert.Equal(43, ai.PlacedChips.Single().Position);
        Assert.Single(ai.BagPhysicalChipIds);
        Assert.Equal(DuckPhase.Night, runtime.State.Phase);
        Assert.Equal(9, human.TotalTwigs);
        Assert.Equal(9, ai.TotalTwigs);
        Assert.Equal(2, human.PermanentFeatherTrail);
        Assert.Equal(2, ai.PermanentFeatherTrail);
        Assert.Equal(21, human.LastNightOutcome!.PrintedSleep);
        Assert.Equal(9, human.LastNightOutcome.PrintedTwigs);
        Assert.Equal(2, human.LastNightOutcome.FeathersAwarded);
        Assert.Equal(24, human.FrozenSleep);
        Assert.Equal(22, ai.FrozenSleep);
        Assert.All(runtime.State.Players, player =>
            Assert.Equal(43, player.PlacedChips.Last().Position));
    }

    [Fact]
    public void Empty_bag_finishes_only_after_the_last_chip_ability_resolves()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "reeds_3");
        var player = runtime.Player("human");

        Execute(Session(runtime), "human", GameActionKind.Explore);

        Assert.True(player.HasFinishedDay);
        Assert.False(player.IsWornOut);
        Assert.Equal(3, player.DayReedsTwigs);
        Assert.Equal(3, player.TotalTwigs);
        Assert.Single(player.PlacedChips);
    }

    [Fact]
    public void Settle_keeps_the_final_occupied_position_and_clears_private_pending_state()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "tailwind_4", "seeds");
        var match = Session(runtime);
        Execute(match, "human", GameActionKind.Explore);
        var player = runtime.Player("human");
        player.KnownNextPhysicalChipIds.Add(player.BagPhysicalChipIds[0]);
        player.SplashProtectionArmed = true;
        player.LogSlowdownPending = true;

        Execute(match, "human", GameActionKind.Settle);

        Assert.Equal(4, player.Position);
        Assert.True(player.HasFinishedDay);
        Assert.False(player.IsWornOut);
        Assert.Empty(player.KnownNextPhysicalChipIds);
        Assert.False(player.SplashProtectionArmed);
        Assert.False(player.LogSlowdownPending);
    }

    [Fact]
    public void Night_resolves_once_only_after_both_players_finish()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        SetBag(runtime, "human", "seeds");
        SetBag(runtime, "ai", "seeds");
        var match = Session(runtime);

        Execute(match, "human", GameActionKind.Explore);
        Assert.Equal(DuckPhase.Adventure, runtime.State.Phase);
        Assert.Null(runtime.Player("human").LastNightOutcome);

        Execute(match, "ai", GameActionKind.Explore);

        Assert.Equal(DuckPhase.Night, runtime.State.Phase);
        Assert.All(runtime.State.Players, player => Assert.NotNull(player.LastNightOutcome));
        Assert.All(runtime.State.Players, player => Assert.True(player.IsSleepFrozen));
        var historyCount = runtime.State.History.Count;
        var awardCount = runtime.State.PublicAwards.Count;
        foreach (var playerId in new[] { "human", "ai" })
        {
            var actions = match.GetLegalActions(playerId);
            Assert.Contains(actions, action => action.Kind == GameActionKind.FinishDream);
            Assert.DoesNotContain(actions, action => action.Kind == GameActionKind.Explore || action.Kind == GameActionKind.Settle);
            match.GetSnapshot(playerId);
        }
        Assert.Equal(historyCount, runtime.State.History.Count);
        Assert.Equal(awardCount, runtime.State.PublicAwards.Count);
    }

    [Fact]
    public void Day_ten_hides_commits_then_reveals_the_whole_beat_in_fixed_order()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        runtime.State.Day = 10;
        runtime.State.FinalDayDecisionBeat = 1;
        var humanIds = SetBag(runtime, "human", "seeds", "tailwind_2");
        var aiIds = SetBag(runtime, "ai", "tailwind_2", "seeds", "seeds");
        var match = Session(runtime);
        var humanAction = Assert.Single(match.GetLegalActions("human"));
        var aiAction = Assert.Single(match.GetLegalActions("ai"));
        var beforeBeat = match.GetSnapshot("human");

        var afterFirstCommit = match.Execute("human", humanAction);

        Assert.True(afterFirstCommit.AwaitingFinalDayDecisions);
        Assert.Equal(beforeBeat.CurrentEvent.DefinitionId, afterFirstCommit.CurrentEvent.DefinitionId);
        Assert.Equal(beforeBeat.OwnBag.Select(chip => chip.PhysicalChipId),
            afterFirstCommit.OwnBag.Select(chip => chip.PhysicalChipId));
        Assert.Equal(
            beforeBeat.Players.Select(player => (player.Id, player.Position, player.BagCount, player.HasFinishedDay)),
            afterFirstCommit.Players.Select(player => (player.Id, player.Position, player.BagCount, player.HasFinishedDay)));
        Assert.Empty(runtime.Player("human").PlacedChips);
        Assert.Empty(runtime.State.History);
        Assert.Single(runtime.State.FinalDayCommits);

        match.Execute("ai", aiAction);

        Assert.Empty(runtime.State.FinalDayCommits);
        Assert.Equal(2, runtime.State.FinalDayDecisionBeat);
        Assert.Equal(humanIds[0], runtime.Player("human").PlacedChips.Single().PhysicalChipId);
        Assert.Equal(aiIds[0], runtime.Player("ai").PlacedChips.Single().PhysicalChipId);
        Assert.Equal(new[] { "human", "ai" }, runtime.State.History.Select(entry => entry.ActorId));

        var humanSettle = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Settle);
        var aiExplore = match.GetLegalActions("ai").Single(action => action.Kind == GameActionKind.Explore);
        match.Execute("human", humanSettle);
        Assert.False(runtime.Player("human").HasFinishedDay);
        match.Execute("ai", aiExplore);

        Assert.True(runtime.Player("human").HasFinishedDay);
        Assert.False(runtime.Player("ai").HasFinishedDay);
        Assert.Equal(3, runtime.State.FinalDayDecisionBeat);

        Execute(match, "ai", GameActionKind.Settle);
        Assert.Equal(DuckPhase.Finished, runtime.State.Phase);
        Assert.NotNull(runtime.State.FinalResult);
    }

    [Fact]
    public void Day_ten_reveal_resolves_the_whole_frozen_cohort_when_the_first_duck_wears_out()
    {
        var runtime = RuntimeWithEvent(DuckWorldEventType.HomeBeforeDark);
        runtime.State.Day = 10;
        runtime.State.FinalDayDecisionBeat = 1;
        SetBag(runtime, "human", "fallen_log", "seeds");
        SetBag(runtime, "ai", "reeds_3", "seeds");
        var human = runtime.Player("human");
        var ai = runtime.Player("ai");
        human.Exhaustion = 5;
        var match = Session(runtime);
        var humanExplore = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore);
        var aiExplore = match.GetLegalActions("ai").Single(action => action.Kind == GameActionKind.Explore);

        match.Execute("human", humanExplore);
        Assert.Empty(human.PlacedChips);
        Assert.Empty(ai.PlacedChips);

        match.Execute("ai", aiExplore);

        Assert.True(human.HasFinishedDay);
        Assert.True(human.IsWornOut);
        Assert.Equal(6, human.Exhaustion);
        Assert.Single(human.PlacedChips);
        Assert.False(ai.HasFinishedDay);
        Assert.False(ai.IsWornOut);
        Assert.Equal(1, ai.Position);
        Assert.Equal(3, ai.DayReedsTwigs);
        Assert.Equal(3, ai.TotalTwigs);
        Assert.Single(ai.PlacedChips);
        Assert.Empty(runtime.State.FinalDayCommits);
        Assert.Equal(2, runtime.State.FinalDayDecisionBeat);
        Assert.Equal(DuckPhase.Adventure, runtime.State.Phase);
        Assert.Empty(match.GetLegalActions("human"));
        Assert.Contains(match.GetLegalActions("ai"), action => action.Kind == GameActionKind.Explore);
        Assert.Equal(new[] { "human", "human", "ai" },
            runtime.State.History.Select(entry => entry.ActorId));
    }

    private static DuckMatchRuntime RuntimeWithEvent(DuckWorldEventType eventType)
    {
        var runtime = DuckMatchRuntime.Create(101, rulesRevision:
            eventType == DuckWorldEventType.SunlitSignboards ? 2 : 3);
        runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex] = runtime.Rules.WorldEvents
            .Single(worldEvent => worldEvent.EventType == eventType).DefinitionId;
        foreach (var player in runtime.State.Players)
            player.GuideProtectionAvailable = eventType == DuckWorldEventType.FriendlyGuide;
        return runtime;
    }

    private static int[] SetBag(DuckMatchRuntime runtime, string playerId, params string[] definitionIds)
    {
        var player = runtime.Player(playerId);
        player.Inventory.Clear();
        player.BagPhysicalChipIds.Clear();
        player.KnownNextPhysicalChipIds.Clear();
        player.PlacedChips.Clear();
        player.PlacedHelpfulTypes.Clear();
        player.Position = player.EffectiveStart;
        player.Exhaustion = 0;
        player.SafeExhaustionMaximum = 5;
        player.ActiveFlock = 0;
        player.SplashProtectionArmed = false;
        player.LogSlowdownPending = false;
        player.PocketDriftwoodAwarded = false;
        player.FlowersPlaced = 0;
        player.DayReedsTwigs = 0;
        player.DayEventTwigs = 0;
        player.TotalTwigs = 0;
        player.HasFinishedDay = false;
        player.IsWornOut = false;
        player.GuideProtectionAvailable = runtime.CurrentEvent.EventType == DuckWorldEventType.FriendlyGuide;

        foreach (var definitionId in definitionIds)
        {
            runtime.Rules.Encounter(definitionId);
            var chip = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, definitionId);
            player.Inventory.Add(chip);
            player.BagPhysicalChipIds.Add(chip.PhysicalChipId);
        }
        return player.BagPhysicalChipIds.ToArray();
    }

    private static MatchSession<DuckMatchView> Session(DuckMatchRuntime runtime)
    {
        return new MatchSession<DuckMatchView>(runtime);
    }

    private static DuckMatchView Execute(
        MatchSession<DuckMatchView> match,
        string playerId,
        GameActionKind kind)
    {
        var action = match.GetLegalActions(playerId).Single(candidate => candidate.Kind == kind);
        return match.Execute(playerId, action);
    }
}
