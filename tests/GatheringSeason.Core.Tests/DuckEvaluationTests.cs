using System.Text.Json;
using System.Text.Json.Nodes;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Match;
using GatheringSeason.Evaluation;
using GatheringSeason.Evaluation.Policies;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckEvaluationTests
{
    [Fact]
    public void Seed_20_trailer_keeps_a_final_Day_recovery_open_instead_of_banking_shelter_ten()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "Duck", "seed20-day10-revision1.json"));
        var save = JsonSerializer.Deserialize<DuckSaveData>(json,
            new JsonSerializerOptions { IncludeFields = true })!;
        var match = DuckSaves.Restore(save);
        var observation = match.GetSnapshot("ai");
        var player = observation.Players.Single(candidate => candidate.Id == "ai");
        Assert.Equal(1, save.RulesVersion);
        Assert.Equal(10, observation.Day);
        Assert.Equal(10, player.Position);
        Assert.Equal(32, player.TotalTwigs);
        Assert.Single(player.PlacedChips);
        Assert.Equal(40, observation.Players.Single(candidate => candidate.Id == "human").TotalTwigs);

        var decision = new DuckNormalPolicy().Evaluate(observation, match.GetLegalActions("ai"));
        Assert.Equal(GameActionKind.Explore, decision.Action.Kind);
        Assert.Contains("keeps a possible recovery open", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Telemetry_accounts_for_every_authoritative_night_and_inventory_change()
    {
        var result = Run(seed: 17, "baseline", "cautious").Result;

        Assert.Equal(Enumerable.Range(1, 10), result.Days.Select(day => day.Day));
        Assert.All(result.Days, day => Assert.Equal(2, day.Players.Count));
        foreach (var playerId in new[] { "human", "ai" })
        {
            var cumulativePurchases = 0;
            foreach (var day in result.Days)
            {
                var player = day.Players.Single(candidate => candidate.PlayerId == playerId);
                cumulativePurchases += player.Purchases.Count;
                Assert.Equal(day.Day, player.Night.Day);
                Assert.Equal(player.Night.PrintedTwigs - player.Night.BramblesPenalty + player.Night.DreamTwigs,
                    player.EndOfNightTotalTwigs - player.PreNightTotalTwigs);
                Assert.Equal(day.Day >= 5 ? 14 + cumulativePurchases : 13 + cumulativePurchases,
                    player.EndInventoryComposition.Values.Sum());
                Assert.True(player.Adventure.DrawCount >= 1);
                Assert.True(player.Adventure.MaxSpace >= player.Adventure.RestSpace);
                Assert.Equal(!player.Adventure.IsWornOut, player.Adventure.IsSafe);
                if (day.Day < 10)
                {
                    Assert.NotNull(player.NextDayStart);
                    var next = result.Days[day.Day].Players.Single(candidate => candidate.PlayerId == playerId).Start;
                    Assert.Equal(next.EffectiveStart, player.NextDayStart!.EffectiveStart);
                    Assert.Equal(next.DawnTwigDeficit, player.NextDayStart.DawnTwigDeficit);
                    Assert.Equal(next.DawnFeathersAwarded, player.NextDayStart.DawnFeathersAwarded);
                }
                else
                {
                    Assert.Null(player.NextDayStart);
                }
            }
        }
    }

    [Fact]
    public void Pre_night_standings_keep_adventure_twigs_and_remove_final_dream_twigs()
    {
        var result = Run(0, "normal", "reeds-heavy").Result;
        var playerDays = result.Days.SelectMany(day => day.Players).ToArray();

        Assert.Contains(playerDays, player => player.Night.ReedsTwigs > 0);
        Assert.Contains(playerDays, player => player.Night.EventTwigs > 0);
        Assert.All(playerDays, player => Assert.Equal(
            player.Night.PrintedTwigs - player.Night.BramblesPenalty + player.Night.DreamTwigs,
            player.EndOfNightTotalTwigs - player.PreNightTotalTwigs));
        Assert.All(result.Days[9].Players, player => Assert.True(player.Night.DreamTwigs > 0));
    }

    [Fact]
    public void Unspent_sleep_is_captured_before_finish_dream_expires_it()
    {
        var result = Run(0, "baseline", "cautious").Result;

        Assert.Contains(result.Days.Take(9).SelectMany(day => day.Players), player => player.UnspentReward > 0);
        Assert.All(result.Days[9].Players, player => Assert.Equal(0, player.UnspentReward));
    }

    [Fact]
    public void Fixed_seed_policy_seats_and_schedule_have_deterministic_game_telemetry()
    {
        var first = Run(42, "baseline", "cautious", includeActions: true).Result;
        var second = Run(42, "baseline", "cautious", includeActions: true).Result;

        Assert.Equal(DeterministicSignature(first), DeterministicSignature(second));
        Assert.Equal(ActionSignature(first), ActionSignature(second));
    }

    [Fact]
    public void Swapped_seats_map_logical_policies_without_renaming_players()
    {
        var original = Run(8, "cautious", "adventurous", swapped: false).Result;
        var swapped = Run(8, "cautious", "adventurous", swapped: true).Result;

        Assert.Equal(new EvaluationSeatAssignment("policyA", "policyB", false), original.SeatAssignment);
        Assert.Equal(new EvaluationSeatAssignment("policyB", "policyA", true), swapped.SeatAssignment);
        Assert.All(original.Days, day =>
        {
            Assert.Equal("cautious", day.Players.Single(player => player.PlayerId == "human").PolicyId);
            Assert.Equal("adventurous", day.Players.Single(player => player.PlayerId == "ai").PolicyId);
        });
        Assert.All(swapped.Days, day =>
        {
            Assert.Equal("adventurous", day.Players.Single(player => player.PlayerId == "human").PolicyId);
            Assert.Equal("cautious", day.Players.Single(player => player.PlayerId == "ai").PolicyId);
        });
    }

    [Fact]
    public void Full_game_captures_each_phase_boundary_once_including_finished_night_ten()
    {
        var outcome = Run(91, "baseline", "baseline", includeActions: true);

        Assert.Equal(10, outcome.Result.Days.Count);
        Assert.Equal(10, outcome.Result.Days.SelectMany(day => day.Players)
            .Count(player => player.Night.DreamTwigs >= 0) / 2);
        Assert.All(outcome.Result.Days.Take(9).SelectMany(day => day.Players),
            player => Assert.Equal(0, player.Night.DreamTwigs));
        Assert.NotEmpty(outcome.Result.Final.WinnerIds);
        Assert.Equal(2, outcome.Result.Final.Standings.Count);
        Assert.All(outcome.Result.Final.Standings, standing =>
            Assert.Equal(10, outcome.FinalSave.Day));
        Assert.Equal(DuckPhase.Finished, outcome.FinalSave.Phase);
        Assert.Equal(10, outcome.FinalSave.Players.Select(player => player.LastNightOutcome!.Day).Distinct().Single());
        Assert.Equal(20, outcome.Result.Days.SelectMany(day => day.Players).Select(player => player.Night).Count());
    }

    [Theory]
    [InlineData(EvaluationSchedule.Cli, "human,ai", "ai,human", "human,ai")]
    [InlineData(EvaluationSchedule.Reversed, "ai,human", "ai,human", "ai,human")]
    [InlineData(EvaluationSchedule.AlternatingDay, "human,ai", "ai,human", "human,ai")]
    public void Scheduler_supplies_only_own_observation_and_executes_exact_issued_actions(
        EvaluationSchedule schedule,
        string dayOneOrder,
        string dayTwoOrder,
        string dayThreeOrder)
    {
        var humanPolicy = new RecordingPolicy("human-policy", "human");
        var aiPolicy = new RecordingPolicy("ai-policy", "ai");
        var request = new EvaluationMatchRequest(33, 0, humanPolicy, aiPolicy, false, schedule,
            "tests@fixed", IncludeActions: true);

        var result = new EvaluationRunner().Run(request).Result;

        Assert.True(humanPolicy.Decisions > 0);
        Assert.True(aiPolicy.Decisions > 0);
        Assert.NotNull(result.Actions);
        Assert.Equal(dayOneOrder, FirstAdventurePlayers(result, 1));
        Assert.Equal(dayTwoOrder, FirstAdventurePlayers(result, 2));
        Assert.Equal(dayThreeOrder, FirstAdventurePlayers(result, 3));
        Assert.All(result.Actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.ActionId));
            Assert.False(string.IsNullOrWhiteSpace(action.Reason));
        });
        Assert.Contains(result.Actions, action => action.HiddenFinalDayCommit);
    }

    [Fact]
    public void Star_telemetry_records_currency_free_purchases_and_the_actual_Sunshine_choice()
    {
        var outcome = Run(10, "sunshine-fresh", "sunshine-warm");
        Assert.Equal(2, outcome.Result.SchemaVersion);
        Assert.Equal(7, outcome.Result.RulesVersion);
        Assert.Equal("Stars", outcome.Result.Currency);
        var sunshine = Assert.Single(outcome.Result.Days, day => day.EventDefinitionId == "glorious_sunshine");
        var fresh = sunshine.Players.Single(player => player.PolicyId == "sunshine-fresh");
        var warm = sunshine.Players.Single(player => player.PolicyId == "sunshine-warm");
        Assert.Equal(GatheringSeason.Core.Ducks.Definitions.DuckGloriousSunshineBenefit.FreshAir, fresh.SunshineChoice);
        Assert.Equal(GatheringSeason.Core.Ducks.Definitions.DuckGloriousSunshineBenefit.WarmDreams, warm.SunshineChoice);
        Assert.Equal(0, fresh.Night.GloriousSunshineReward);
        Assert.Equal(2, warm.Night.GloriousSunshineReward);
        Assert.Contains(outcome.Result.Days.SelectMany(day => day.Players).SelectMany(player => player.Purchases),
            purchase => purchase.OfferDefinitionId == "seeds" && purchase.Price == 0);
        foreach (var standing in outcome.Result.Final.Standings)
        {
            var saved = outcome.FinalSave.Players.Single(player => player.Id == standing.PlayerId);
            Assert.Equal(saved.FrozenSleep, standing.FrozenNightTenReward);
        }
    }

    [Fact]
    public void Matched_event_probes_use_the_same_Dawn_state_without_mutating_the_source_game()
    {
        DuckSaveData? source = null;
        var request = new EvaluationMatchRequest(42, 0, EvaluationPolicies.Create("cautious"),
            EvaluationPolicies.Create("adventurous"), false, EvaluationSchedule.AlternatingDay, "probe-test");
        new EvaluationRunner().Run(request, save => source ??= save);
        Assert.NotNull(source);
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        var before = JsonSerializer.Serialize(source, jsonOptions);
        var probes = EventProbeRunner.Run(source!, request).ToArray();
        Assert.Equal(before, JsonSerializer.Serialize(source, jsonOptions));
        Assert.Equal(9, probes.Select(probe => probe.EventDefinitionId).Distinct().Count());
        Assert.Single(probes.Select(probe => probe.SourceStateHash).Distinct());
        Assert.All(probes, probe =>
        {
            Assert.Equal(2, probe.Day);
            Assert.Equal(2, probe.Players.Count);
            Assert.All(probe.Players, player =>
            {
                Assert.InRange(player.RestSpace, 1, 43);
                Assert.True(player.DrawCount > 0);
                Assert.Equal(2, player.Night.Day);
            });
        });
        var sunshine = probes.Where(probe => probe.EventDefinitionId == "glorious_sunshine").ToArray();
        Assert.Equal(new[] { "fresh-fresh", "fresh-warm", "warm-fresh", "warm-warm" }, sunshine.Select(probe => probe.Variant));
        Assert.All(probes.GroupBy(probe => probe.Players[0].PlayerId), group =>
            Assert.Single(group.Select(probe => probe.Players[0].StartSpace).Distinct()));
        var repeat = EventProbeRunner.Run(source!, request).ToArray();
        Assert.Equal(JsonSerializer.Serialize(probes), JsonSerializer.Serialize(repeat));
    }

    private static string FirstAdventurePlayers(EvaluationMatchResult result, int day) => string.Join(",",
        result.Actions!
            .Where(action => action.Day == day && action.Phase == DuckPhase.Adventure)
            .Take(2)
            .Select(action => action.PlayerId));

    private static EvaluationRunOutcome Run(
        int seed,
        string policyA,
        string policyB,
        bool swapped = false,
        EvaluationSchedule schedule = EvaluationSchedule.Cli,
        bool includeActions = false) =>
        new EvaluationRunner().Run(new EvaluationMatchRequest(
            seed, 0, EvaluationPolicies.Create(policyA), EvaluationPolicies.Create(policyB),
            swapped, schedule, "tests@fixed", includeActions));

    private static string DeterministicSignature(EvaluationMatchResult result)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(result))!;
        RemoveTiming(node);
        return node.ToJsonString();
    }

    private static void RemoveTiming(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("Timings");
            obj.Remove("PolicyElapsedMicroseconds");
            obj.Remove("ExecuteElapsedMicroseconds");
            foreach (var child in obj.ToArray())
                if (child.Value != null) RemoveTiming(child.Value);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child != null) RemoveTiming(child);
        }
    }

    private static string ActionSignature(EvaluationMatchResult result) => JsonSerializer.Serialize(
        result.Actions!.Select(action => new
        {
            action.Sequence,
            action.Day,
            action.Phase,
            action.PlayerId,
            action.PolicyId,
            action.ActionId,
            action.Kind,
            action.DefinitionId,
            action.Label,
            action.Reason,
            action.HiddenFinalDayCommit
        }));

    private sealed class RecordingPolicy : IEvaluationPolicy
    {
        private readonly string _viewerId;
        private readonly IEvaluationPolicy _delegate = EvaluationPolicies.Create("cautious");

        public RecordingPolicy(string id, string viewerId)
        {
            Id = id;
            _viewerId = viewerId;
        }

        public string Id { get; }
        public string Description => "Test policy that verifies its observation boundary.";
        public int Decisions { get; private set; }

        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            Assert.Equal(_viewerId, observation.ViewerId);
            Assert.NotEmpty(legalActions);
            Decisions++;
            var decision = _delegate.Decide(observation, legalActions);
            Assert.Contains(legalActions, action => ReferenceEquals(action, decision.Action));
            return decision;
        }
    }
}
