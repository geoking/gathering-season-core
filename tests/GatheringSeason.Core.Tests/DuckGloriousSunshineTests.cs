using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckGloriousSunshineTests
{
    [Fact]
    public void Normal_explains_an_early_Fresh_Air_and_late_Warm_Dreams_choice()
    {
        var policy = new DuckNormalPolicy();
        var earlyRuntime = SunshineRuntime(day: 1);
        var early = new MatchSession<DuckMatchView>(earlyRuntime);
        var earlyDecision = policy.Evaluate(early.GetSnapshot("ai"), early.GetLegalActions("ai"));
        Assert.StartsWith("Fresh Air", earlyDecision.Action.Label);
        Assert.Contains("Early in the season", earlyDecision.Reason);

        var lateRuntime = SunshineRuntime(day: 10);
        var late = new MatchSession<DuckMatchView>(lateRuntime);
        var lateDecision = policy.Evaluate(late.GetSnapshot("ai"), late.GetLegalActions("ai"));
        Assert.StartsWith("Warm Dreams", lateDecision.Action.Label);
        Assert.Contains("final conversion", lateDecision.Reason);
    }

    [Fact]
    public void Both_public_choices_are_required_once_before_Adventure_begins()
    {
        var runtime = SunshineRuntime();
        var match = new MatchSession<DuckMatchView>(runtime);

        var humanChoices = match.GetLegalActions("human");
        Assert.Equal(2, humanChoices.Count);
        Assert.All(humanChoices, action => Assert.Equal(GameActionKind.ChooseEventBenefit, action.Kind));
        match.Execute("human", humanChoices.Single(action => action.Label.StartsWith("Fresh Air")));

        Assert.Empty(match.GetLegalActions("human"));
        var publicHuman = match.GetSnapshot("ai").Players.Single(player => player.Id == "human");
        Assert.True(publicHuman.HasGloriousSunshineChoice);
        Assert.Equal(DuckGloriousSunshineBenefit.FreshAir, publicHuman.GloriousSunshineBenefit);

        var aiChoice = match.GetLegalActions("ai").Single(action => action.Label.StartsWith("Warm Dreams"));
        match.Execute("ai", aiChoice);

        Assert.Single(match.GetLegalActions("human"), action => action.Kind == GameActionKind.Explore);
        Assert.Single(match.GetLegalActions("ai"), action => action.Kind == GameActionKind.Explore);
        Assert.Equal(8, runtime.Player("human").SafeExhaustionMaximum);
        Assert.Equal(5, runtime.Player("ai").SafeExhaustionMaximum);
        Assert.Equal(2, runtime.State.History.Count(entry => entry.Message.Contains("Glorious Sunshine")));
    }

    [Fact]
    public void Fresh_Air_and_Goose_share_the_live_and_planning_transition()
    {
        var rules = DuckRules.V1;
        var goose = rules.Encounter("grumpy_goose");
        var before = new DuckAdventureState(0, 7, 8, 0, false, false, false, false, 0, 0);
        var unprotected = DuckAdventureRules.ApplyEncounter(before, goose, DuckWorldEventType.GloriousSunshine);
        var protectedState = new DuckAdventureState(0, 7, 8, 0, true, false, false, false, 0, 0);
        var protectedGoose = DuckAdventureRules.ApplyEncounter(
            protectedState, goose, DuckWorldEventType.GloriousSunshine);

        Assert.Equal(8, unprotected.State.Exhaustion);
        Assert.Equal(7, unprotected.State.SafeExhaustionMaximum);
        Assert.True(unprotected.WearsOut);
        Assert.Equal(8, protectedGoose.State.Exhaustion);
        Assert.Equal(8, protectedGoose.State.SafeExhaustionMaximum);
        Assert.False(protectedGoose.WearsOut);
        Assert.True(protectedGoose.NuisanceSuppressed);
    }

    [Fact]
    public void Revision_six_Fresh_Air_can_wear_out_before_Day_five()
    {
        var runtime = SunshineRuntime(rulesRevision: 6);
        var human = runtime.Player("human");
        var obstacleIds = human.Inventory
            .Where(chip => runtime.Rules.Encounter(chip.DefinitionId).IsObstacle)
            .Select(chip => chip.PhysicalChipId)
            .ToArray();
        var wishIds = human.Inventory
            .Where(chip => runtime.Rules.Encounter(chip.DefinitionId).IsHelpful)
            .Select(chip => chip.PhysicalChipId)
            .ToArray();
        human.BagPhysicalChipIds.Clear();
        human.BagPhysicalChipIds.AddRange(obstacleIds);
        human.BagPhysicalChipIds.AddRange(wishIds);
        var match = new MatchSession<DuckMatchView>(runtime);

        match.Execute("human", match.GetLegalActions("human").Single(action => action.Label.StartsWith("Fresh Air")));
        match.Execute("ai", match.GetLegalActions("ai").Single(action => action.Label.StartsWith("Warm Dreams")));

        Assert.Equal(1, runtime.State.Day);
        Assert.Equal(7, human.SafeExhaustionMaximum);
        Assert.Equal(7, DuckSaves.Restore(DuckSaves.Capture(match)).GetSnapshot("human")
            .Players.Single(player => player.Id == "human").SafeExhaustionMaximum);
        for (var draw = 1; draw <= 7; draw++)
        {
            match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore));
            Assert.False(human.IsWornOut);
            Assert.Equal(draw, human.Exhaustion);
        }

        match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore));

        Assert.True(human.IsWornOut);
        Assert.Equal(8, human.Exhaustion);
        Assert.Equal(7, human.SafeExhaustionMaximum);
    }

    [Fact]
    public void Revision_six_Goose_lowers_Fresh_Air_to_six_unless_Splash_protects_it()
    {
        var goose = DuckRules.V1.Encounter("grumpy_goose");
        var unprotected = DuckAdventureRules.ApplyEncounter(
            new DuckAdventureState(0, 6, 7, 0, false, false, false, false, 0, 0),
            goose,
            DuckWorldEventType.GloriousSunshine);
        var protectedGoose = DuckAdventureRules.ApplyEncounter(
            new DuckAdventureState(0, 6, 7, 0, true, false, false, false, 0, 0),
            goose,
            DuckWorldEventType.GloriousSunshine);

        Assert.Equal(6, unprotected.State.SafeExhaustionMaximum);
        Assert.True(unprotected.WearsOut);
        Assert.Equal(7, protectedGoose.State.SafeExhaustionMaximum);
        Assert.False(protectedGoose.WearsOut);
        Assert.True(protectedGoose.NuisanceSuppressed);
    }

    [Fact]
    public void Warm_Dreams_is_added_before_wear_halving_and_Most_Rested()
    {
        var safeRuntime = SunshineRuntime();
        Complete(safeRuntime.Player("human"), position: 1, wornOut: false,
            DuckGloriousSunshineBenefit.WarmDreams);
        Complete(safeRuntime.Player("ai"), position: 3, wornOut: false,
            DuckGloriousSunshineBenefit.FreshAir);
        var safeOutcomes = DuckNightResolver.Calculate(safeRuntime.State, safeRuntime.Rules);

        Assert.Equal(11, safeOutcomes["human"].FrozenSleep);
        Assert.True(safeOutcomes["human"].IsMostRested);
        Assert.Equal(5, safeOutcomes["ai"].FrozenSleep);
        Assert.False(safeOutcomes["ai"].IsMostRested);

        var wornRuntime = SunshineRuntime();
        Complete(wornRuntime.Player("human"), position: 1, wornOut: true,
            DuckGloriousSunshineBenefit.WarmDreams);
        Complete(wornRuntime.Player("ai"), position: 2, wornOut: false,
            DuckGloriousSunshineBenefit.FreshAir);

        var outcomes = DuckNightResolver.Calculate(wornRuntime.State, wornRuntime.Rules);

        Assert.Equal(8, outcomes["human"].GloriousSunshineSleep);
        Assert.Equal(11, outcomes["human"].SleepBeforeWear);
        Assert.Equal(5, outcomes["human"].FrozenSleep);
        Assert.False(outcomes["human"].IsMostRested);
        Assert.Equal(0, outcomes["ai"].GloriousSunshineSleep);
        Assert.Equal(3, outcomes["ai"].FrozenSleep);
        Assert.True(outcomes["ai"].IsMostRested);
    }

    [Fact]
    public void Warm_Dreams_counts_in_final_conversion_and_match_end()
    {
        var runtime = SunshineRuntime(day: 10);
        Complete(runtime.Player("human"), position: 1, wornOut: false,
            DuckGloriousSunshineBenefit.WarmDreams);
        Complete(runtime.Player("ai"), position: 3, wornOut: false,
            DuckGloriousSunshineBenefit.WarmDreams);

        DuckNightResolver.Resolve(runtime.State, runtime.Rules);

        Assert.Equal(DuckPhase.Finished, runtime.State.Phase);
        Assert.Equal(11, runtime.Player("human").FrozenSleep);
        Assert.Equal(2, runtime.Player("human").LastNightOutcome!.DreamTwigs);
        Assert.Equal(13, runtime.Player("ai").FrozenSleep);
        Assert.Equal(4, runtime.Player("ai").LastNightOutcome!.DreamTwigs);
        Assert.NotNull(runtime.State.FinalResult);
    }

    [Fact]
    public void Sunshine_choice_resets_at_the_next_Dawn()
    {
        var runtime = SunshineRuntime();
        runtime.State.Phase = DuckPhase.DayComplete;
        foreach (var player in runtime.State.Players)
        {
            player.HasGloriousSunshineChoice = true;
            player.GloriousSunshineBenefit = DuckGloriousSunshineBenefit.FreshAir;
            player.SafeExhaustionMaximum = 8;
            player.HasFinishedDay = true;
            player.HasFinishedDream = true;
        }

        DuckDayPreparation.BeginNextDay(runtime);

        Assert.Equal(2, runtime.State.Day);
        Assert.All(runtime.State.Players, player =>
        {
            Assert.False(player.HasGloriousSunshineChoice);
            Assert.Equal(5, player.SafeExhaustionMaximum);
        });
    }

    [Fact]
    public void Mid_choice_save_round_trip_preserves_the_public_gate()
    {
        var runtime = SunshineRuntime();
        var match = new MatchSession<DuckMatchView>(runtime);
        match.Execute("human", match.GetLegalActions("human").Single(action => action.Label.StartsWith("Warm Dreams")));

        var restored = DuckSaves.Restore(DuckSaves.Capture(match));
        var players = restored.GetSnapshot("ai").Players;

        Assert.True(players.Single(player => player.Id == "human").HasGloriousSunshineChoice);
        Assert.False(players.Single(player => player.Id == "ai").HasGloriousSunshineChoice);
        Assert.Empty(restored.GetLegalActions("human"));
        Assert.Equal(2, restored.GetLegalActions("ai").Count);
    }

    [Fact]
    public void Save_validation_rejects_Fresh_Air_without_its_threshold_and_a_premature_Day10_commit()
    {
        var early = new MatchSession<DuckMatchView>(SunshineRuntime());
        early.Execute("human", early.GetLegalActions("human").Single(action => action.Label.StartsWith("Fresh Air")));
        var wrongThreshold = DuckSaves.Capture(early);
        wrongThreshold.Players.Single(player => player.Id == "human").SafeExhaustionMaximum = 5;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(wrongThreshold));

        var final = new MatchSession<DuckMatchView>(SunshineRuntime(day: 10));
        final.Execute("human", final.GetLegalActions("human").Single(action => action.Label.StartsWith("Warm Dreams")));
        var prematureCommit = DuckSaves.Capture(final);
        prematureCommit.FinalDayCommits.Add(new DuckFinalDayCommitSaveData
        {
            Beat = prematureCommit.FinalDayDecisionBeat,
            PlayerId = "human",
            ActionKind = GameActionKind.Explore
        });
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(prematureCommit));
    }

    [Fact]
    public void Revision_two_keeps_legacy_Sunlit_Signboards()
    {
        var runtime = DuckMatchRuntime.Create(41, rulesRevision: 2);
        var save = DuckSaves.Capture(new MatchSession<DuckMatchView>(runtime));

        Assert.Equal(2, save.RulesVersion);
        Assert.Contains("sunlit_signboards", save.WorldEventDeckDefinitionIds);
        Assert.DoesNotContain("glorious_sunshine", save.WorldEventDeckDefinitionIds);
        Assert.Equal(save.WorldEventDeckDefinitionIds,
            DuckSaves.Capture(DuckSaves.Restore(save)).WorldEventDeckDefinitionIds);
    }

    [Fact]
    public void Revision_five_Fresh_Air_save_restores_its_old_threshold_and_board()
    {
        var match = new MatchSession<DuckMatchView>(SunshineRuntime(rulesRevision: 5));
        match.Execute("human", match.GetLegalActions("human").Single(action => action.Label.StartsWith("Fresh Air (+3")));
        match.Execute("ai", match.GetLegalActions("ai").Single(action => action.Label.StartsWith("Warm Dreams")));

        var restored = DuckSaves.Restore(DuckSaves.Capture(match));
        var view = restored.GetSnapshot("human");

        Assert.Equal(5, view.RulesRevision);
        Assert.Equal(8, view.Players.Single(player => player.Id == "human").SafeExhaustionMaximum);
        Assert.True(view.Rules.BoardSpaceAt(21).IsShelter);
        Assert.True(view.Rules.BoardSpaceAt(26).IsShelter);
        Assert.Equal(6, view.Rules.BoardSpaceAt(28).Twigs);
    }

    private static DuckMatchRuntime SunshineRuntime(int day = 1, int rulesRevision = 3)
    {
        var runtime = DuckMatchRuntime.Create(317, rulesRevision: rulesRevision);
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
        var sunshineIndex = runtime.State.WorldEventDeckDefinitionIds.IndexOf("glorious_sunshine");
        (runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex],
            runtime.State.WorldEventDeckDefinitionIds[sunshineIndex]) =
            (runtime.State.WorldEventDeckDefinitionIds[sunshineIndex],
                runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex]);
        return runtime;
    }

    private static void Complete(
        DuckPlayerState player,
        int position,
        bool wornOut,
        DuckGloriousSunshineBenefit benefit)
    {
        var chip = player.Inventory.First();
        player.Position = position;
        player.PlacedChips.Add(new DuckPlacedChipState(chip.PhysicalChipId, position, nuisanceSuppressed: false));
        player.HasFinishedDay = true;
        player.IsWornOut = wornOut;
        player.HasGloriousSunshineChoice = true;
        player.GloriousSunshineBenefit = benefit;
        player.SafeExhaustionMaximum = benefit == DuckGloriousSunshineBenefit.FreshAir ? 8 : 5;
    }
}
