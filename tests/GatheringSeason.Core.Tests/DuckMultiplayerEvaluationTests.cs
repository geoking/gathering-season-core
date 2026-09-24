using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal(3, first.Days.Count(day => day.CollectiveAttempt));
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
