using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;
using Xunit.Abstractions;

namespace GatheringSeason.Core.Tests;

public sealed class DuckNormalPolicyTests
{
    private readonly DuckNormalPolicy _policy = new();
    private readonly ITestOutputHelper _output;

    public DuckNormalPolicyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Exact_preview_changes_the_decision_but_unknown_bag_order_does_not()
    {
        var seedPreview = AdventureScenario(position: 3, exhaustion: 4, bag: new[] { "seeds", "grumpy_goose" },
            knownDefinitionId: "seeds");
        var goosePreview = AdventureScenario(position: 3, exhaustion: 4, bag: new[] { "seeds", "grumpy_goose" },
            knownDefinitionId: "grumpy_goose");

        Assert.Equal(GameActionKind.Explore, Choose(seedPreview).Kind);
        var gooseDecision = Evaluate(goosePreview);
        Assert.Equal(GameActionKind.Settle, gooseDecision.Action.Kind);
        Assert.Contains("Signpost preview", gooseDecision.Reason, StringComparison.Ordinal);

        var firstUnknown = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        var reversedUnknown = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        reversedUnknown.Runtime.Player("human").BagPhysicalChipIds.Reverse();

        Assert.Equal(
            Evaluate(firstUnknown).Action.Id,
            Evaluate(reversedUnknown).Action.Id);
        Assert.Equal(
            firstUnknown.Match.GetSnapshot("human").OwnBag.Select(chip => chip.DefinitionId),
            reversedUnknown.Match.GetSnapshot("human").OwnBag.Select(chip => chip.DefinitionId));
    }

    [Fact]
    public void Two_exact_previews_support_a_safe_draw_then_a_stop_without_peeking_beyond_them()
    {
        var scenario = AdventureScenario(
            position: 3,
            exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose", "tailwind_2" },
            knownDefinitionId: "seeds",
            rulesRevision: 2);
        scenario.Runtime.State.WorldEventDeckDefinitionIds[scenario.Runtime.State.CurrentEventIndex] =
            "sunlit_signboards";
        var player = scenario.Runtime.Player("human");
        var goose = player.BagPhysicalChipIds.First(id =>
            player.Inventory.Single(chip => chip.PhysicalChipId == id).DefinitionId == "grumpy_goose");
        player.KnownNextPhysicalChipIds.Add(goose);

        var first = Evaluate(scenario);
        Assert.Equal(GameActionKind.Explore, first.Action.Kind);
        scenario.Match.Execute("human", first.Action);

        var second = Evaluate(scenario);
        Assert.Equal(GameActionKind.Settle, second.Action.Kind);
        Assert.Contains("Signpost preview is Grumpy Goose", second.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Future_Signpost_information_adds_value_only_when_the_event_will_reveal_a_chip()
    {
        var remaining = new[] { "tailwind_4", "grumpy_goose", "grumpy_goose", "grumpy_goose" };
        var signpost = AdventureScenario(
            position: 4,
            exhaustion: 4,
            bag: new[] { "signpost" }.Concat(remaining).ToArray(),
            knownDefinitionId: "signpost");
        var sameMovementWithoutPreview = AdventureScenario(
            position: 4,
            exhaustion: 4,
            bag: new[] { "tailwind_2" }.Concat(remaining).ToArray(),
            knownDefinitionId: "tailwind_2");
        var mistedSignpost = AdventureScenario(
            position: 4,
            exhaustion: 4,
            bag: new[] { "signpost" }.Concat(remaining).ToArray(),
            knownDefinitionId: "signpost");
        mistedSignpost.Runtime.State.WorldEventDeckDefinitionIds[
            mistedSignpost.Runtime.State.CurrentEventIndex] = "thick_morning_mist";

        Assert.Equal(GameActionKind.Explore, Choose(signpost).Kind);
        Assert.Equal(GameActionKind.Settle, Choose(sameMovementWithoutPreview).Kind);
        Assert.Equal(GameActionKind.Settle, Choose(mistedSignpost).Kind);
    }

    [Fact]
    public void Protected_Goose_still_wears_out_when_its_Exhaustion_exceeds_five()
    {
        var scenario = AdventureScenario(position: 3, exhaustion: 5,
            bag: new[] { "grumpy_goose" }, knownDefinitionId: "grumpy_goose");
        scenario.Runtime.Player("human").SplashProtectionArmed = true;

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Settle, decision.Action.Kind);
        Assert.Contains("Exhaustion 5", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_keeps_a_safe_shelter_but_draws_from_the_neighbouring_ordinary_space_into_it()
    {
        var atShelter = AdventureScenario(position: 4, exhaustion: 0,
            bag: new[] { "seeds" }, knownDefinitionId: "seeds");
        var beforeShelter = AdventureScenario(position: 3, exhaustion: 0,
            bag: new[] { "seeds" }, knownDefinitionId: "seeds");

        Assert.Equal(GameActionKind.Settle, Choose(atShelter).Kind);
        var explore = Choose(beforeShelter);
        Assert.Equal(GameActionKind.Explore, explore.Kind);
        beforeShelter.Match.Execute("human", explore);
        Assert.Equal(4, beforeShelter.Runtime.Player("human").Position);
    }

    [Fact]
    public void Policy_uses_the_observed_revision_when_a_known_wear_out_would_reach_a_rewarding_shelter()
    {
        var revisionFour = AdventureScenario(
            position: 3,
            exhaustion: 5,
            bag: new[] { "fallen_log" },
            knownDefinitionId: "fallen_log",
            rulesRevision: 4);
        var revisionFive = AdventureScenario(
            position: 3,
            exhaustion: 5,
            bag: new[] { "fallen_log" },
            knownDefinitionId: "fallen_log",
            rulesRevision: 5);
        foreach (var scenario in new[] { revisionFour, revisionFive })
        {
            scenario.Runtime.Player("human").FlowersPlaced = 1;
            scenario.Runtime.Player("human").ActiveFlock = 1;
        }

        var oldDecision = Evaluate(revisionFour);
        var currentDecision = Evaluate(revisionFive);

        Assert.Equal(GameActionKind.Settle, oldDecision.Action.Kind);
        Assert.Equal(GameActionKind.Explore, currentDecision.Action.Kind);
    }

    [Fact]
    public void Multi_draw_plan_leaves_an_early_shelter_to_cross_a_temporary_reward_dip()
    {
        var scenario = AdventureScenario(
            position: 4,
            exhaustion: 0,
            bag: new[] { "seeds", "tailwind_4", "seeds" },
            knownDefinitionId: "seeds");

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("3-draw plan", decision.Reason, StringComparison.Ordinal);
        scenario.Match.Execute("human", decision.Action);
        Assert.Equal(5, scenario.Runtime.Player("human").Position);
        Assert.False(DuckRules.V1.BoardSpaceAt(5).IsShelter);
    }

    [Fact]
    public void Multi_draw_plan_continues_from_an_early_ordinary_space_toward_a_better_rest()
    {
        var scenario = AdventureScenario(
            position: 5,
            exhaustion: 0,
            bag: new[] { "seeds", "tailwind_4" },
            knownDefinitionId: "seeds");

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("2-draw plan", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Early_safe_plateau_does_not_trap_the_policy_after_one_placement()
    {
        var plateau = AdventureScenario(position: 1, exhaustion: 0,
            bag: new[] { "seeds" }, knownDefinitionId: "seeds");

        var decision = Evaluate(plateau);

        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("improves", decision.Reason, StringComparison.Ordinal);
        plateau.Match.Execute("human", decision.Action);
        Assert.Equal(2, plateau.Runtime.Player("human").Position);
    }

    [Fact]
    public void Finished_opponents_public_rest_can_change_the_Most_Rested_decision()
    {
        var defendLead = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        var cannotLead = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        SetFinishedOpponent(defendLead.Runtime, position: 1);
        SetFinishedOpponent(cannotLead.Runtime, position: 9);

        var defendDecision = Evaluate(defendLead);
        var chaseDecision = Evaluate(cannotLead);

        Assert.Equal(GameActionKind.Settle, defendDecision.Action.Kind);
        Assert.Equal(GameActionKind.Explore, chaseDecision.Action.Kind);
        Assert.Contains("Keeping", defendDecision.Reason, StringComparison.Ordinal);
        Assert.Contains("improves", chaseDecision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Worn_out_finished_opponent_cannot_block_the_safe_ducks_Most_Rested_value()
    {
        var safeOpponent = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        var wornOpponent = AdventureScenario(position: 3, exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" });
        SetFinishedOpponent(safeOpponent.Runtime, position: 9, wornOut: false, frozenSleep: 7);
        SetFinishedOpponent(wornOpponent.Runtime, position: 9, wornOut: true, frozenSleep: 7);

        var blocked = Evaluate(safeOpponent);
        var unblocked = Evaluate(wornOpponent);

        Assert.Equal(GameActionKind.Explore, blocked.Action.Kind);
        Assert.Equal(GameActionKind.Settle, unblocked.Action.Kind);
        Assert.Contains("improves", blocked.Reason, StringComparison.Ordinal);
        Assert.Contains("Keeping", unblocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Early_Dream_prefers_movement_while_late_Dream_prefers_direct_Reeds_Twigs()
    {
        var early = DreamScenario(day: 2, sleep: 20);
        var late = DreamScenario(day: 8, sleep: 20);

        var earlyActions = PurchaseComparisonActions(early.Match, "human");
        var lateActions = PurchaseComparisonActions(late.Match, "human");

        Assert.Equal("tailwind_4", _policy.Choose(early.Match.GetSnapshot("human"), earlyActions).DefinitionId);
        Assert.Equal("reeds_1", _policy.Choose(late.Match.GetSnapshot("human"), lateActions).DefinitionId);
    }

    [Fact]
    public void Complete_bundle_beats_the_best_individual_purchase_that_would_consume_the_budget()
    {
        var scenario = DreamScenario(day: 4, sleep: 15);
        var first = _policy.Evaluate(
            scenario.Match.GetSnapshot("human"),
            scenario.Match.GetLegalActions("human"));

        Assert.Equal(GameActionKind.BuyEncounter, first.Action.Kind);
        Assert.NotEqual("tailwind_6", first.Action.DefinitionId);
        Assert.Contains("2-chip Night bundle", first.Reason, StringComparison.Ordinal);

        scenario.Match.Execute("human", first.Action);
        var second = _policy.Choose(
            scenario.Match.GetSnapshot("human"),
            scenario.Match.GetLegalActions("human"));
        Assert.Equal(GameActionKind.BuyEncounter, second.Kind);
        scenario.Match.Execute("human", second);

        var player = scenario.Runtime.Player("human");
        Assert.Equal(2, player.PurchasedEncounterDefinitionIds.Count);
        Assert.Equal(2, player.PurchasedShopTypes.Count);
        var observedPrices = scenario.Match.GetSnapshot("human").ShopOffers
            .ToDictionary(offer => offer.DefinitionId, offer => offer.SleepPrice);
        Assert.True(player.PurchasedEncounterDefinitionIds
            .Select(id => observedPrices[id])
            .Sum() <= 15);
    }

    [Fact]
    public void Dream_finishes_when_the_only_affordable_purchase_would_worsen_a_saturated_bag()
    {
        var scenario = DreamScenario(day: 9, sleep: 3);
        var player = scenario.Runtime.Player("human");
        for (var count = 0; count < 12; count++)
            player.Inventory.Add(new DuckPhysicalChipState(scenario.Runtime.State.NextPhysicalChipId++, "seeds"));

        var actions = scenario.Match.GetLegalActions("human");
        Assert.Contains(actions, action => action.DefinitionId == "seeds");

        var decision = _policy.Evaluate(scenario.Match.GetSnapshot("human"), actions);

        Assert.Equal(GameActionKind.FinishDream, decision.Action.Kind);
        Assert.Contains("without another purchase", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Three_slot_bundle_respects_budget_and_one_purchase_per_type()
    {
        var scenario = DreamScenario(day: 7, sleep: 18);
        var initialSleep = scenario.Runtime.Player("human").RemainingSleep;
        while (true)
        {
            var action = _policy.Choose(
                scenario.Match.GetSnapshot("human"),
                scenario.Match.GetLegalActions("human"));
            if (action.Kind == GameActionKind.FinishDream) break;
            scenario.Match.Execute("human", action);
        }

        var player = scenario.Runtime.Player("human");
        Assert.InRange(player.PurchasedEncounterDefinitionIds.Count, 1, 3);
        Assert.Equal(player.PurchasedEncounterDefinitionIds.Count, player.PurchasedShopTypes.Count);
        var observedPrices = scenario.Match.GetSnapshot("human").ShopOffers
            .ToDictionary(offer => offer.DefinitionId, offer => offer.SleepPrice);
        Assert.True(player.PurchasedEncounterDefinitionIds
            .Select(id => observedPrices[id])
            .Sum() <= initialSleep);
    }

    [Fact]
    public void Dream_bundle_affordability_uses_each_matches_observed_price_revision()
    {
        AssertBundleUsesObservedPrices(DreamScenario(day: 4, sleep: 20));
        AssertBundleUsesObservedPrices(LegacyDreamScenario(day: 4, sleep: 20));
    }

    [Fact]
    public void Final_Day_plan_takes_a_known_safe_step_into_a_high_value_shelter()
    {
        var scenario = AdventureScenario(
            position: 14,
            exhaustion: 0,
            bag: new[] { "tailwind_2" },
            knownDefinitionId: "tailwind_2",
            day: 10);

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("known Tailwind 2", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Iterative_search_reports_its_complete_depth_and_stays_within_the_node_budget()
    {
        var scenario = AdventureScenario(
            position: 1,
            exhaustion: 0,
            bag: DuckRules.V1.OpeningBag.Select(definition => definition.DefinitionId).ToArray());
        var timer = Stopwatch.StartNew();

        var decision = Evaluate(scenario);

        timer.Stop();
        var nodeMatch = Regex.Match(decision.Reason, @"(?:used |\()(?<nodes>[0-9]+) (?:total )?nodes");
        Assert.True(nodeMatch.Success, decision.Reason);
        Assert.InRange(int.Parse(nodeMatch.Groups["nodes"].Value), 1, 8000);
        Assert.Matches(@"[1-6]-draw plan", decision.Reason);
        _output.WriteLine("bounded planning decision: {0:0.000} ms; {1}", timer.Elapsed.TotalMilliseconds, decision.Reason);
    }

    [Fact]
    public void Late_large_bag_search_is_bounded_and_deterministic()
    {
        var bag = new[]
        {
            "brambles", "brambles", "companion", "companion", "companion",
            "fallen_log", "fallen_log", "grumpy_goose", "loose_pebbles", "loose_pebbles",
            "mud_puddle", "mud_puddle", "reeds_1", "reeds_1", "reeds_2",
            "seeds", "seeds", "signpost", "splash", "splash", "splash", "splash",
            "tailwind_2", "tailwind_4", "wildflowers"
        };
        var scenario = AdventureScenario(position: 10, exhaustion: 0, bag: bag, day: 10);
        var view = scenario.Match.GetSnapshot("human");
        var actions = scenario.Match.GetLegalActions("human");
        var timer = Stopwatch.StartNew();

        var first = _policy.Evaluate(view, actions);
        var repeat = _policy.Evaluate(view, actions);

        timer.Stop();
        Assert.Same(first.Action, repeat.Action);
        Assert.Equal(first.Reason, repeat.Reason);
        var nodeMatch = Regex.Match(first.Reason, @"(?:used |\()(?<nodes>[0-9]+) (?:total )?nodes");
        Assert.True(nodeMatch.Success, first.Reason);
        Assert.InRange(int.Parse(nodeMatch.Groups["nodes"].Value), 1, 8000);
        Assert.Matches(@"[3-6]-draw plan", first.Reason);
        _output.WriteLine("two deterministic late-bag decisions: {0:0.000} ms; {1}",
            timer.Elapsed.TotalMilliseconds, first.Reason);
    }

    [Fact]
    public void Final_Day_continues_when_settling_provably_loses_but_an_unknown_draw_can_recover()
    {
        var recoveryChance = AdventureScenario(
            position: 10,
            exhaustion: 4,
            bag: new[] { "reeds_3", "grumpy_goose" },
            day: 10);
        recoveryChance.Runtime.Player("human").TotalTwigs = 10;
        SetPublicOpponentScore(recoveryChance.Runtime, totalTwigs: 16, position: 10);

        var noRecovery = AdventureScenario(
            position: 10,
            exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" },
            day: 10);
        noRecovery.Runtime.Player("human").TotalTwigs = 10;
        SetPublicOpponentScore(noRecovery.Runtime, totalTwigs: 16, position: 10);

        var recoveryDecision = Evaluate(recoveryChance);
        var noRecoveryDecision = Evaluate(noRecovery);

        Assert.Equal(GameActionKind.Explore, recoveryDecision.Action.Kind);
        Assert.Contains("current final score is provably behind", recoveryDecision.Reason, StringComparison.Ordinal);
        Assert.Equal(GameActionKind.Settle, noRecoveryDecision.Action.Kind);
    }

    [Fact]
    public void Final_Day_visible_bag_upper_bound_keeps_a_recovery_open_beyond_the_search_horizon()
    {
        var scenario = AdventureScenario(
            position: 10,
            exhaustion: 0,
            bag: new[]
            {
                "brambles", "brambles", "companion", "companion", "companion",
                "fallen_log", "fallen_log", "grumpy_goose", "loose_pebbles", "loose_pebbles",
                "mud_puddle", "mud_puddle", "reeds_1", "reeds_1", "reeds_2",
                "seeds", "signpost", "splash", "splash", "splash", "splash",
                "tailwind_2", "tailwind_4", "wildflowers"
            },
            day: 10);
        scenario.Runtime.State.WorldEventDeckDefinitionIds[scenario.Runtime.State.CurrentEventIndex] = "still_air";
        scenario.Runtime.Player("human").TotalTwigs = 32;
        SetPublicOpponentScore(scenario.Runtime, totalTwigs: 40, position: 20);

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("keeps a possible recovery open", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_Day_provable_loss_does_not_override_an_exact_lethal_preview()
    {
        var scenario = AdventureScenario(
            position: 10,
            exhaustion: 4,
            bag: new[] { "grumpy_goose", "reeds_3" },
            knownDefinitionId: "grumpy_goose",
            day: 10);
        scenario.Runtime.Player("human").TotalTwigs = 10;
        SetPublicOpponentScore(scenario.Runtime, totalTwigs: 16, position: 10);

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Settle, decision.Action.Kind);
        Assert.Contains("Signpost preview is Grumpy Goose", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_Day_possible_sleep_tiebreak_prevents_a_false_provable_loss()
    {
        var scenario = AdventureScenario(
            position: 10,
            exhaustion: 4,
            bag: new[] { "seeds", "grumpy_goose" },
            day: 10);
        scenario.Runtime.Player("human").TotalTwigs = 10;
        SetPublicOpponentScore(scenario.Runtime, totalTwigs: 15, position: 10);

        var decision = Evaluate(scenario);

        Assert.Equal(GameActionKind.Settle, decision.Action.Kind);
        Assert.DoesNotContain("provably behind", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Purchase_choice_respects_issued_legal_types_and_available_budget()
    {
        var scenario = DreamScenario(day: 4, sleep: 7);
        var view = scenario.Match.GetSnapshot("human");
        var issued = scenario.Match.GetLegalActions("human");

        var decision = _policy.Evaluate(view, issued);

        Assert.Contains(issued, action => ReferenceEquals(action, decision.Action));
        Assert.Equal(GameActionKind.BuyEncounter, decision.Action.Kind);
        Assert.True(decision.Action.Cost <= view.Players.Single(player => player.Id == "human").RemainingSleep);
        Assert.Contains(decision.Action.DefinitionId,
            new[] { "seeds", "tailwind_2", "signpost", "splash", "reeds_1", "companion", "wildflowers" });
        scenario.Match.Execute("human", decision.Action);
        Assert.DoesNotContain(scenario.Match.GetLegalActions("human"), action =>
            action.Kind == GameActionKind.BuyEncounter
            && DuckRules.V1.ShopOffer(action.DefinitionId).ShopType ==
                DuckRules.V1.ShopOffer(decision.Action.DefinitionId).ShopType);
    }

    [Fact]
    public void Pending_Day_ten_commit_does_not_change_the_other_ducks_policy_input_decision()
    {
        var runtime = DuckMatchRuntime.Create(seed: 313, rulesRevision: 3);
        runtime.State.Day = 10;
        runtime.State.FinalDayDecisionBeat = 1;
        PrepareAdventurePlayer(runtime, "human", position: 3, exhaustion: 0, new[] { "seeds", "fallen_log" });
        PrepareAdventurePlayer(runtime, "ai", position: 3, exhaustion: 0, new[] { "seeds", "fallen_log" });
        var match = new MatchSession<DuckMatchView>(runtime);
        var beforeView = match.GetSnapshot("ai");
        var before = _policy.Choose(beforeView, match.GetLegalActions("ai"));

        var humanCommit = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore);
        match.Execute("human", humanCommit);
        var pendingView = match.GetSnapshot("ai");
        var pending = _policy.Choose(pendingView, match.GetLegalActions("ai"));

        Assert.False(beforeView.AwaitingFinalDayDecisions);
        Assert.True(pendingView.AwaitingFinalDayDecisions);
        Assert.Equal(before.Id, pending.Id);
        Assert.Equal(before.Kind, pending.Kind);
        Assert.Equal(
            beforeView.Players.Select(PublicPlayerTuple),
            pendingView.Players.Select(PublicPlayerTuple));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(42)]
    [InlineData(941)]
    public void Normal_policy_completes_seeded_games_with_real_stops_and_portfolio_choices(int seed)
    {
        var first = PlayNormalGame(seed);
        var repeat = PlayNormalGame(seed);

        Assert.Equal(DuckPhase.Finished, first.View.Phase);
        Assert.NotNull(first.View.FinalResult);
        Assert.InRange(first.ActionCount, 1, 3999);
        Assert.True(first.ActionKinds.GetValueOrDefault(GameActionKind.Explore) > 0);
        Assert.True(first.ActionKinds.GetValueOrDefault(GameActionKind.Settle) > 0);
        Assert.Equal(2, first.ActionKinds.GetValueOrDefault(GameActionKind.ChooseEventBenefit));
        Assert.True(first.ActionKinds.GetValueOrDefault(GameActionKind.BuyEncounter) > 0);
        Assert.True(first.PurchaseDefinitionIds.Distinct(StringComparer.Ordinal).Count() >= 2);
        Assert.Equal(first.ActionKinds.OrderBy(pair => pair.Key), repeat.ActionKinds.OrderBy(pair => pair.Key));
        Assert.Equal(first.PurchaseDefinitionIds, repeat.PurchaseDefinitionIds);
        Assert.Equal(
            first.View.FinalResult!.Standings.Select(FinalTuple),
            repeat.View.FinalResult!.Standings.Select(FinalTuple));
        _output.WriteLine(
            "seed {0}: {1} actions, Explore {2}, Settle {3}, purchases {4} ({5} types), winners {6}",
            seed,
            first.ActionCount,
            first.ActionKinds.GetValueOrDefault(GameActionKind.Explore),
            first.ActionKinds.GetValueOrDefault(GameActionKind.Settle),
            first.ActionKinds.GetValueOrDefault(GameActionKind.BuyEncounter),
            first.PurchaseDefinitionIds.Distinct(StringComparer.Ordinal).Count(),
            string.Join(" & ", first.View.FinalResult.WinnerIds));
    }

    private GameAction Choose(AdventureTestScenario scenario) =>
        _policy.Choose(scenario.Match.GetSnapshot("human"), scenario.Match.GetLegalActions("human"));

    private DuckPolicyDecision Evaluate(AdventureTestScenario scenario) =>
        _policy.Evaluate(scenario.Match.GetSnapshot("human"), scenario.Match.GetLegalActions("human"));

    private static AdventureTestScenario AdventureScenario(
        int position,
        int exhaustion,
        string[] bag,
        string? knownDefinitionId = null,
        int day = 1,
        int rulesRevision = 3)
    {
        var runtime = DuckMatchRuntime.Create(seed: 211, rulesRevision: rulesRevision);
        runtime.State.Day = day;
        runtime.State.FinalDayDecisionBeat = day == DuckMatchSettings.StandardDays ? 1 : 0;
        runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex] = "home_before_dark";
        runtime.Player("human").GuideProtectionAvailable = false;
        PrepareAdventurePlayer(runtime, "human", position, exhaustion, bag);
        var player = runtime.Player("human");
        if (knownDefinitionId != null)
        {
            var known = player.BagPhysicalChipIds.First(id =>
                player.Inventory.Single(chip => chip.PhysicalChipId == id).DefinitionId == knownDefinitionId);
            player.BagPhysicalChipIds.Remove(known);
            player.BagPhysicalChipIds.Insert(0, known);
            player.KnownNextPhysicalChipIds.Add(known);
        }
        return new AdventureTestScenario(runtime, new MatchSession<DuckMatchView>(runtime));
    }

    private static void PrepareAdventurePlayer(
        DuckMatchRuntime runtime,
        string playerId,
        int position,
        int exhaustion,
        string[] bag)
    {
        var player = runtime.Player(playerId);
        player.Inventory.Clear();
        player.BagPhysicalChipIds.Clear();
        player.KnownNextPhysicalChipIds.Clear();
        player.PlacedChips.Clear();
        player.PlacedHelpfulTypes.Clear();
        player.Position = position;
        player.Exhaustion = exhaustion;
        player.SafeExhaustionMaximum = 5;
        player.HasFinishedDay = false;
        player.IsWornOut = false;
        var placedId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(placedId, "seeds"));
        player.PlacedChips.Add(new DuckPlacedChipState(placedId, position, nuisanceSuppressed: false));
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
        foreach (var definitionId in bag)
        {
            var physicalId = runtime.State.NextPhysicalChipId++;
            player.Inventory.Add(new DuckPhysicalChipState(physicalId, definitionId));
            player.BagPhysicalChipIds.Add(physicalId);
        }
    }

    private static void SetFinishedOpponent(
        DuckMatchRuntime runtime,
        int position,
        bool wornOut = false,
        int? frozenSleep = null)
    {
        var opponent = runtime.Player("ai");
        opponent.Position = position;
        opponent.HasFinishedDay = true;
        opponent.IsWornOut = wornOut;
        opponent.IsSleepFrozen = frozenSleep.HasValue;
        opponent.FrozenSleep = frozenSleep ?? 0;
        opponent.PlacedChips.Clear();
    }

    private static void SetPublicOpponentScore(DuckMatchRuntime runtime, int totalTwigs, int position)
    {
        var opponent = runtime.Player("ai");
        opponent.TotalTwigs = totalTwigs;
        opponent.Position = position;
        opponent.HasFinishedDay = false;
        opponent.IsWornOut = false;
        opponent.PlacedChips.Clear();
        var physicalId = runtime.State.NextPhysicalChipId++;
        opponent.Inventory.Add(new DuckPhysicalChipState(physicalId, "seeds"));
        opponent.PlacedChips.Add(new DuckPlacedChipState(physicalId, position, nuisanceSuppressed: false));
        opponent.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
    }

    private static DreamTestScenario DreamScenario(int day, int sleep)
    {
        var runtime = DuckMatchRuntime.Create(seed: 227, rulesRevision: 3);
        runtime.State.Day = day;
        runtime.State.Phase = DuckPhase.Night;
        foreach (var player in runtime.State.Players)
        {
            player.FrozenSleep = sleep;
            player.RemainingSleep = sleep;
            player.IsSleepFrozen = true;
            player.HasFinishedDream = false;
        }
        return new DreamTestScenario(runtime, new MatchSession<DuckMatchView>(runtime));
    }

    private static DreamTestScenario LegacyDreamScenario(int day, int sleep)
    {
        var match = MatchSession.CreateDuck(seed: 228);
        while (match.GetSnapshot("human").Day < day)
        {
            FinishAdventure(match);
            foreach (var playerId in new[] { "human", "ai" })
                ExecuteFirst(match, playerId, GameActionKind.FinishDream);
            ExecuteFirst(match, "human", GameActionKind.NextDay);
        }
        FinishAdventure(match);

        var save = DuckSaves.Capture(match);
        save.RulesVersion = 1;
        save.WorldEventDeckDefinitionIds[save.WorldEventDeckDefinitionIds.IndexOf("glorious_sunshine")] =
            "sunlit_signboards";
        foreach (var player in save.Players)
        {
            player.HasGloriousSunshineChoice = false;
            if (player.LastNightOutcome != null) player.LastNightOutcome.GloriousSunshineSleep = 0;
        }
        foreach (var player in save.Players)
        {
            player.FrozenSleep = sleep;
            player.RemainingSleep = sleep;
            player.LastNightOutcome!.PrintedSleep += sleep - player.LastNightOutcome.SleepBeforeWear;
            player.LastNightOutcome.SleepBeforeWear = sleep;
            player.LastNightOutcome!.FrozenSleep = sleep;
        }
        var restored = DuckSaves.Restore(save);
        return new DreamTestScenario(restored.DuckRuntime(), restored);
    }

    private void AssertBundleUsesObservedPrices(DreamTestScenario scenario)
    {
        var initialView = scenario.Match.GetSnapshot("human");
        var initialSleep = initialView.Players.Single(player => player.Id == "human").RemainingSleep;
        var observedPrices = initialView.ShopOffers
            .ToDictionary(offer => offer.DefinitionId, offer => offer.SleepPrice);

        var plan = _policy.Evaluate(initialView, scenario.Match.GetLegalActions("human"));
        Assert.Equal(GameActionKind.BuyEncounter, plan.Action.Kind);
        var plannedSpend = Regex.Match(plan.Reason, @"(?<spend>[0-9]+) remaining Sleep spend");
        var plannedCount = Regex.Match(plan.Reason, @"(?<count>[0-9]+)-chip Night bundle");
        Assert.True(plannedSpend.Success);
        Assert.True(plannedCount.Success);

        while (true)
        {
            var action = _policy.Choose(
                scenario.Match.GetSnapshot("human"),
                scenario.Match.GetLegalActions("human"));
            if (action.Kind == GameActionKind.FinishDream) break;
            Assert.Equal(observedPrices[action.DefinitionId], action.Cost);
            scenario.Match.Execute("human", action);
        }

        var player = scenario.Runtime.Player("human");
        var paid = player.PurchasedEncounterDefinitionIds.Sum(id => observedPrices[id]);
        Assert.Equal(initialSleep - paid, player.RemainingSleep);
        Assert.Equal(int.Parse(plannedSpend.Groups["spend"].Value), paid);
        Assert.Equal(int.Parse(plannedCount.Groups["count"].Value), player.PurchasedEncounterDefinitionIds.Count);
        Assert.True(paid <= initialSleep);
    }

    private static void FinishAdventure(MatchSession<DuckMatchView> match)
    {
        foreach (var playerId in new[] { "human", "ai" })
        {
            var choice = match.GetLegalActions(playerId)
                .FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit);
            if (choice != null) match.Execute(playerId, choice);
        }
        foreach (var playerId in new[] { "human", "ai" })
        {
            if (match.GetLegalActions(playerId).Any(action => action.Kind == GameActionKind.Explore))
                ExecuteFirst(match, playerId, GameActionKind.Explore);
        }
        while (match.GetSnapshot("human").Phase == DuckPhase.Adventure)
        {
            foreach (var playerId in new[] { "human", "ai" })
            {
                if (match.GetLegalActions(playerId).Any(action => action.Kind == GameActionKind.Settle))
                    ExecuteFirst(match, playerId, GameActionKind.Settle);
            }
        }
    }

    private static void ExecuteFirst(
        MatchSession<DuckMatchView> match,
        string playerId,
        GameActionKind kind)
    {
        match.Execute(playerId, match.GetLegalActions(playerId).First(action => action.Kind == kind));
    }

    private static IReadOnlyList<GameAction> PurchaseComparisonActions(
        MatchSession<DuckMatchView> match,
        string playerId)
    {
        return match.GetLegalActions(playerId).Where(action =>
            action.Kind == GameActionKind.FinishDream
            || action.DefinitionId == "tailwind_4"
            || action.DefinitionId == "reeds_1").ToArray();
    }

    private NormalGameResult PlayNormalGame(int seed)
    {
        var match = MatchSession.CreateDuck(seed);
        var kinds = new Dictionary<GameActionKind, int>();
        var purchases = new List<string>();
        for (var step = 0; step < 4000; step++)
        {
            var view = match.GetSnapshot("human");
            if (view.Phase == DuckPhase.Finished)
                return new NormalGameResult(view, kinds.Values.Sum(), kinds, purchases);
            var acted = false;
            foreach (var playerId in new[] { "human", "ai" })
            {
                var actions = match.GetLegalActions(playerId);
                if (actions.Count == 0) continue;
                var action = _policy.Choose(match.GetSnapshot(playerId), actions);
                Assert.Contains(actions, issued => ReferenceEquals(issued, action));
                kinds[action.Kind] = kinds.GetValueOrDefault(action.Kind) + 1;
                if (action.Kind == GameActionKind.BuyEncounter) purchases.Add(action.DefinitionId);
                match.Execute(playerId, action);
                acted = true;
            }
            if (!acted) throw new InvalidOperationException("Policy game stalled before final scoring.");
        }
        throw new InvalidOperationException("Policy game exceeded its action bound.");
    }

    private static (string Id, int Position, int Exhaustion, int BagCount, bool Finished, bool Worn)
        PublicPlayerTuple(DuckPlayerView player) =>
        (player.Id, player.Position, player.Exhaustion, player.BagCount, player.HasFinishedDay, player.IsWornOut);

    private static (string PlayerId, int Rank, int Twigs, int Sleep, int DreamTwigs, bool Winner)
        FinalTuple(DuckFinalStanding standing) =>
        (standing.PlayerId, standing.Rank, standing.TotalTwigs, standing.FrozenNightTenSleep,
            standing.DreamTwigs, standing.IsWinner);

    private sealed record AdventureTestScenario(DuckMatchRuntime Runtime, MatchSession<DuckMatchView> Match);
    private sealed record DreamTestScenario(DuckMatchRuntime Runtime, MatchSession<DuckMatchView> Match);
    private sealed record NormalGameResult(
        DuckMatchView View,
        int ActionCount,
        IReadOnlyDictionary<GameActionKind, int> ActionKinds,
        IReadOnlyList<string> PurchaseDefinitionIds);

}
