using System.Text.Json;
using System.Text.Json.Nodes;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using GatheringSeason.Evaluation;
using GatheringSeason.Evaluation.Policies;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckMultiplayerEvaluationTests
{
    [Theory]
    [InlineData(2, 7)]
    [InlineData(3, 13)]
    [InlineData(4, 17)]
    public void Assignments_rotate_each_style_and_deduplicate_all_Normal(int count, int expectedMatches)
    {
        var assignments = MultiplayerStudyRunner.Assignments(count);
        Assert.Equal(expectedMatches, assignments.Count);
        Assert.Single(assignments, assignment => assignment.Scenario == "all-normal");
        Assert.Equal(expectedMatches, assignments.Select(assignment => assignment.Scenario).Distinct().Count());
        var ids = new[] { "human", "ai", "ai-2", "ai-3" }.Take(count).ToArray();
        foreach (var id in ids)
        {
            Assert.Single(assignments, assignment => assignment.Scenario.StartsWith("focal-movement-heavy-", StringComparison.Ordinal)
                && assignment.Styles[id] == "movement-heavy");
            Assert.Single(assignments, assignment => assignment.Scenario.StartsWith("focal-reeds-heavy-", StringComparison.Ordinal)
                && assignment.Styles[id] == "reeds-heavy");
            Assert.Equal(count == 2 ? 1 : 2, assignments.Count(assignment => assignment.Scenario.StartsWith("mixed-", StringComparison.Ordinal)
                && assignment.Styles[id] == "movement-heavy"));
            Assert.Equal(count == 2 ? 1 : 2, assignments.Count(assignment => assignment.Scenario.StartsWith("mixed-", StringComparison.Ordinal)
                && assignment.Styles[id] == "reeds-heavy"));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Seeded_multiplayer_study_records_complete_authoritative_days(int count)
    {
        var assignment = MultiplayerStudyRunner.Assignments(count).First(candidate => candidate.Scenario.StartsWith("mixed-", StringComparison.Ordinal));
        var runner = new MultiplayerStudyRunner();
        var first = runner.Run(42, count, assignment, "tests@fixed");
        var repeat = runner.Run(42, count, assignment, "tests@fixed");
        Assert.Equal(Signature(first), Signature(repeat));
        Assert.Equal(count, first.Players.Count);
        Assert.Equal(Enumerable.Range(1, 10), first.Days.Select(day => day.Day));
        Assert.Equal(first.Days.Count(day => day.EventId is "all_tucked_in" or "home_before_dark" or "shared_supper"),
            first.Days.Count(day => day.CollectiveAttempt));
        Assert.All(first.Days, day =>
        {
            Assert.Equal(count, day.Players.Count);
            Assert.All(day.Players, player =>
            {
                Assert.InRange(player.RestSpace, 1, 43);
                Assert.True(player.Draws > 0);
                Assert.True(player.EndLeaderDeficit >= 0);
                Assert.Equal(player.OasisReached, player.OasisSafe || player.OasisWorn);
                Assert.False(player.OasisSafe && player.OasisWorn);
            });
            if (day.CollectiveTriggered) Assert.True(day.CollectiveAttempt);
        });
        Assert.True(first.ActionCount >= first.Players.Sum(player => player.DrawActions + player.PurchaseActions));
        Assert.Equal(first.ActionCount, first.Players.Sum(player => player.ActionCount));
        Assert.NotEmpty(first.WinnerIds);
        Assert.All(first.WinnerIds, id => Assert.Contains(first.Players, player => player.PlayerId == id && player.Win));
    }

    [Fact]
    public void Multiplayer_options_validate_count_and_required_provenance()
    {
        Assert.Throws<ArgumentException>(() => MultiplayerStudyOptions.Parse(new[] { "--players", "5", "--source-label", "test" }));
        Assert.Throws<ArgumentException>(() => MultiplayerStudyOptions.Parse(new[] { "--players", "4" }));
        var options = MultiplayerStudyOptions.Parse(new[] { "--players", "3", "--seed-count", "24", "--source-label", "test" });
        Assert.Equal(new[] { 3 }, options.PlayerCounts);
        Assert.Equal(24, options.SeedCount);
    }

    [Fact]
    public void Buying_styles_share_the_current_Normal_adventure_decision()
    {
        var match = MatchSession.CreateDuck(42, new DuckMatchSettings(4));
        var first = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore);
        match.Execute("human", first);
        var view = match.GetSnapshot("human");
        var legal = match.GetLegalActions("human");
        var normal = EvaluationPolicies.Create("normal").Decide(view, legal).Action.Id;
        Assert.Equal(normal, EvaluationPolicies.Create("movement-heavy").Decide(view, legal).Action.Id);
        Assert.Equal(normal, EvaluationPolicies.Create("reeds-heavy").Decide(view, legal).Action.Id);
        foreach (var style in new[] { "movement-first-mixed", "flowers-first", "seeds-first", "sunshine-fresh", "sunshine-warm" })
            Assert.Equal(normal, EvaluationPolicies.Create(style).Decide(view, legal).Action.Id);
    }

    [Theory]
    [InlineData(2, 21)]
    [InlineData(3, 34)]
    [InlineData(4, 49)]
    public void Expanded_matrix_rotates_every_focal_style_and_every_ordered_mixed_pair(int count, int expected)
    {
        var styles = new[] { "movement-heavy", "reeds-heavy", "movement-first-mixed", "flowers-first", "seeds-first",
            "cautious-stop", "adventurous-stop", "sunshine-fresh", "sunshine-warm" };
        var assignments = MultiplayerStudyRunner.Assignments(count, styles, allMixedSeats: true);
        Assert.Equal(expected, assignments.Count);
        Assert.Equal(count * (count - 1), assignments.Count(assignment => assignment.Scenario.StartsWith("mixed-")));
        foreach (var style in styles)
            Assert.Equal(count, assignments.Count(assignment => assignment.Scenario.StartsWith($"focal-{style}-")));
        Assert.Equal(expected, assignments.Select(assignment => assignment.Scenario).Distinct().Count());
    }

    [Fact]
    public void Expanded_options_reject_unknown_duplicate_or_Normal_focal_policies()
    {
        foreach (var styles in new[] { "normal", "reeds-heavy,reeds-heavy", "unknown", "" })
            Assert.Throws<ArgumentException>(() => MultiplayerStudyOptions.Parse(new[]
                { "--source-label", "test", "--focal-policies", styles }));
        var options = MultiplayerStudyOptions.Parse(new[] { "--source-label", "test", "--focal-policies",
            "cautious-stop,movement-first-mixed", "--all-mixed-seats" });
        Assert.Equal(new[] { "cautious-stop", "movement-first-mixed" }, options.FocalStyles);
        Assert.True(options.AllMixedSeats);
    }

    [Theory]
    [InlineData("cautious-stop")]
    [InlineData("adventurous-stop")]
    public void Stopping_diagnostics_keep_Normal_shopping_and_event_choices(string style)
    {
        var match = MatchSession.CreateDuck(10);
        var normal = EvaluationPolicies.Create("normal");
        var diagnostic = EvaluationPolicies.Create(style);
        var sawShopping = false;
        var sawSunshine = false;
        for (var step = 0; step < 4000 && match.GetSnapshot("human").Phase != DuckPhase.Finished; step++)
            foreach (var id in new[] { "human", "ai" })
            {
                var actions = match.GetLegalActions(id);
                if (actions.Count == 0) continue;
                var view = match.GetSnapshot(id);
                var expected = normal.Decide(view, actions);
                if (actions.Any(action => action.Kind is GameActionKind.BuyEncounter or GameActionKind.ChooseEventBenefit))
                {
                    Assert.Same(expected.Action, diagnostic.Decide(view, actions).Action);
                    sawShopping |= actions.Any(action => action.Kind == GameActionKind.BuyEncounter);
                    sawSunshine |= actions.Any(action => action.Kind == GameActionKind.ChooseEventBenefit);
                }
                match.Execute(id, expected.Action);
            }
        Assert.True(sawShopping);
        Assert.True(sawSunshine);
    }

    [Fact]
    public void Extended_multiplayer_telemetry_reconciles_rewards_and_the_BASE_deck()
    {
        var runner = new MultiplayerStudyRunner();
        var result = runner.Run(10, 4, MultiplayerStudyRunner.Assignments(4).First(), "test");
        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal(8, result.RulesRevision);
        Assert.Equal(10, result.Days.Select(day => day.EventId).Distinct().Count());
        foreach (var day in result.Days)
            foreach (var player in day.Players)
            {
                Assert.Equal(day.Day, player.Night.Day);
                Assert.Equal(player.Draws, player.PlacedComposition.Values.Sum());
                Assert.Equal(player.ReedsTwigs, player.Night.ReedsTwigs);
                Assert.Equal(player.FrozenStars, player.Night.FrozenReward);
                Assert.Equal(player.WornOut, player.Exhaustion > player.SafeExhaustionMaximum);
                Assert.Equal(player.StartTwigs + player.Night.TotalTwigsEarned + player.Night.DreamTwigs, player.EndTwigs);
                Assert.Equal(day.EventId == "glorious_sunshine", player.SunshineChoice.HasValue);
            }
        foreach (var final in result.Players)
            Assert.Equal(final.TotalTwigs, result.Days.Last().Players.Single(player => player.PlayerId == final.PlayerId).EndTwigs);
    }

    [Theory]
    [InlineData("cautious-stop", 3, 3, "tailwind_6", GameActionKind.Settle, GameActionKind.Explore)]
    [InlineData("adventurous-stop", 10, 4, "grumpy_goose", GameActionKind.Explore, GameActionKind.Settle)]
    public void Stopping_diagnostics_delegate_authorised_previews_even_when_the_threshold_disagrees(
        string style, int position, int exhaustion, string knownDefinition,
        GameActionKind thresholdAction, GameActionKind normalAction)
    {
        var runtime = DuckMatchRuntime.Create(211, rulesRevision: 8);
        runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex] = "home_before_dark";
        var player = runtime.Player("human");
        player.Inventory.Clear();
        player.BagPhysicalChipIds.Clear();
        player.PlacedChips.Clear();
        player.PlacedHelpfulTypes.Clear();
        player.KnownNextPhysicalChipIds.Clear();
        player.GuideProtectionAvailable = false;
        player.Position = position;
        player.Exhaustion = exhaustion;
        player.SafeExhaustionMaximum = 5;
        var placedId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(placedId, "signpost"));
        player.PlacedChips.Add(new DuckPlacedChipState(placedId, position, false));
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Signpost);
        var knownId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(knownId, knownDefinition));
        player.BagPhysicalChipIds.Add(knownId);
        var match = new MatchSession<DuckMatchView>(runtime);
        var actions = match.GetLegalActions("human");
        var diagnostic = EvaluationPolicies.Create(style);
        Assert.Equal(thresholdAction, diagnostic.Decide(match.GetSnapshot("human"), actions).Action.Kind);

        player.KnownNextPhysicalChipIds.Add(knownId);
        var observation = match.GetSnapshot("human");
        var expected = EvaluationPolicies.Create("normal").Decide(observation, actions);
        Assert.Equal(normalAction, expected.Action.Kind);
        Assert.Same(expected.Action, diagnostic.Decide(observation, actions).Action);
    }

    private static string Signature(MultiplayerStudyResult result)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(result))!.AsObject();
        node.Remove("DurationMicroseconds");
        foreach (var player in node["Players"]!.AsArray())
        {
            player!.AsObject().Remove("PolicyMicroseconds");
            player.AsObject().Remove("ExecuteMicroseconds");
        }
        return node.ToJsonString();
    }
}
