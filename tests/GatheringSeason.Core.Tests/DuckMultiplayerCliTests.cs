using System.Text.Json;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed partial class DuckCliTests
{
    [Theory]
    [InlineData("--players", "1")]
    [InlineData("--players=5", "")]
    [InlineData("--players", "three")]
    [InlineData("--wish-set", "set-2")]
    public void Multiplayer_launch_rejects_unsupported_settings(string option, string value)
    {
        var arguments = value.Length == 0 ? new[] { option } : new[] { option, value };
        var result = Run("", arguments);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage: GatheringSeason.Cli", result.Error);
        Assert.DoesNotContain("Gathering Season · 10 Days", result.Output);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Seeded_multiplayer_demo_finishes_reproducibly_and_continue_uses_saved_seats(int count)
    {
        using var firstFiles = new SaveFiles();
        using var replayFiles = new SaveFiles();
        var first = Run("", "--seed", "42", "--players", count.ToString(), "--demo-game", "--save", firstFiles.Path);
        var replay = Run("", "--seed=42", $"--players={count}", "--demo-game", "--save", replayFiles.Path);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, replay.ExitCode);
        Assert.Equal(first.Output.Replace(firstFiles.Path, "<save>"), replay.Output.Replace(replayFiles.Path, "<save>"));
        Assert.Equal(File.ReadAllBytes(firstFiles.Path), File.ReadAllBytes(replayFiles.Path));
        Assert.Equal(DuckPhase.Finished, firstFiles.Read().Phase);
        Assert.Equal(new[] { "human", "ai", "ai-2", "ai-3" }.Take(count), firstFiles.Read().Players.Select(player => player.Id));

        var continued = Run("", "--continue", "--inspect", "--players", "2", "--save", firstFiles.Path);
        Assert.Equal(0, continued.ExitCode);
        Assert.Contains($"continuing saved game · {count} players · Wish Set set-1", continued.Output);
        Assert.DoesNotContain("Opening recipe", continued.Output);
        Assert.Equal(count, firstFiles.Read().Players.Count);
    }

    [Fact]
    public void Normal_mode_blocks_every_CPU_private_view_and_hides_inventory_composition()
    {
        var result = Run("pouch\nview:ai\nview:ai-2\nview:ai-3\nstatus\nhistory\nq\n",
            "--seed", "42", "--players", "4", "--no-save");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, Count(result.Output, "private view is unavailable"));
        Assert.Contains("Opening recipe (13 chips)", result.Output);
        Assert.DoesNotContain("Remaining pouch", result.Output);
        Assert.DoesNotContain("Owned inventory", result.Output);
        Assert.DoesNotContain("Private Signpost preview for ai", result.Output);
        Assert.DoesNotContain("pouch 13 chips", result.Output);
    }

    [Fact]
    public void Developer_mode_lists_and_accepts_all_configured_seats()
    {
        using var files = new SaveFiles();
        var result = Run("view:ai-3\nai-3:1\nq\n", "--seed", "42", "--players", "4",
            "--two-player", "--save", files.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Seats: human, ai, ai-2, ai-3", result.Output);
        Assert.Contains("ai-3:1. Explore", result.Output);
        Assert.Single(files.Read().Players.Single(player => player.Id == "ai-3").PlacedChips);
        Assert.Empty(files.Read().Players.Single(player => player.Id == "human").PlacedChips);
    }

    [Fact]
    public void Current_night_purchase_receipt_disappears_after_Dawn()
    {
        using var files = new SaveFiles();
        var match = MatchSession.CreateDuck(42, new DuckMatchSettings(3));
        foreach (var id in new[] { "human", "ai", "ai-2" })
            ExecuteKind(match, id, GameActionKind.Explore);
        foreach (var id in new[] { "human", "ai", "ai-2" })
            ExecuteKind(match, id, GameActionKind.Settle);
        foreach (var id in new[] { "human", "ai", "ai-2" })
        {
            var buy = match.GetLegalActions(id).First(action => action.Kind == GameActionKind.BuyEncounter);
            match.Execute(id, buy);
        }
        files.Write(DuckSaves.Capture(match));
        var current = Run("night\nhistory\nq\n", "--continue", "--two-player", "--save", files.Path);
        Assert.Equal(0, current.ExitCode);
        Assert.Contains("Current Night purchases entering tomorrow's pouch:", current.Output);
        Assert.Contains("  AI 2:", current.Output);
        Assert.DoesNotContain(" buys Wish ", current.Output);

        foreach (var id in new[] { "human", "ai", "ai-2" })
            ExecuteKind(match, id, GameActionKind.FinishDream);
        ExecuteKind(match, "human", GameActionKind.NextDay);
        files.Write(DuckSaves.Capture(match));
        var afterDawn = Run("night\nhistory\npouch\nq\n", "--continue", "--two-player", "--save", files.Path);
        Assert.Equal(0, afterDawn.ExitCode);
        Assert.DoesNotContain("Current Night purchases entering tomorrow's pouch:", afterDawn.Output);
        Assert.DoesNotContain(" buys Wish ", afterDawn.Output);
        Assert.DoesNotContain("Owned inventory", afterDawn.Output);
    }

    [Fact]
    public void Day_ten_CPU_choices_use_one_precommit_cohort()
    {
        using var files = new SaveFiles();
        var match = MatchSession.CreateDuck(123, new DuckMatchSettings(4));
        var ids = new[] { "human", "ai", "ai-2", "ai-3" };
        for (var step = 0; step < 1000 && match.GetSnapshot("human").Day < 10; step++)
        {
            foreach (var id in ids)
            {
                var actions = match.GetLegalActions(id);
                if (actions.Count == 0) continue;
                var action = actions.FirstOrDefault(item => item.Kind == GameActionKind.Settle)
                    ?? actions.FirstOrDefault(item => item.Kind == GameActionKind.FinishDream)
                    ?? actions[0];
                match.Execute(id, action);
            }
        }
        Assert.Equal(10, match.GetSnapshot("human").Day);
        foreach (var id in ids)
        {
            var choices = match.GetLegalActions(id);
            if (choices.Any(action => action.Kind == GameActionKind.ChooseEventBenefit))
                match.Execute(id, choices.First());
        }
        foreach (var id in ids) ExecuteKind(match, id, GameActionKind.Explore);
        var saved = DuckSaves.Capture(match);
        files.Write(saved);

        var expected = DuckSaves.Restore(saved);
        ExecuteKind(expected, "human", GameActionKind.Explore);
        var policy = new DuckNormalPolicy();
        var cpuChoices = ids.Skip(1).Select(id =>
        {
            var view = expected.GetSnapshot(id);
            return (Id: id, Action: policy.Choose(view, expected.GetLegalActions(id)));
        }).ToArray();
        foreach (var choice in cpuChoices) expected.Execute(choice.Id, choice.Action);

        var actual = Run("1\nq\n", "--continue", "--save", files.Path);
        Assert.Equal(0, actual.ExitCode);
        Assert.Equal(JsonSerializer.Serialize(DuckSaves.Capture(expected), SaveJson),
            JsonSerializer.Serialize(files.Read(), SaveJson));
    }

    [Fact]
    public void Normal_demo_uses_each_latest_observation_on_days_one_through_nine()
    {
        using var files = new SaveFiles();
        var actual = Run("", "--seed", "42", "--players", "3", "--demo-game", "--save", files.Path);
        Assert.Equal(0, actual.ExitCode);

        var expected = MatchSession.CreateDuck(42, new DuckMatchSettings(3));
        var policy = new DuckNormalPolicy();
        for (var step = 0; step < 4000 && expected.GetSnapshot("human").Phase != DuckPhase.Finished; step++)
        {
            var view = expected.GetSnapshot("human");
            var seats = view.Players.Select(player => player.Id).ToArray();
            if (view.Day == 10 && view.Phase == DuckPhase.Adventure
                && seats.Any(id => expected.GetLegalActions(id).Any(action => action.Kind is GameActionKind.Explore or GameActionKind.Settle)))
            {
                var cohort = seats.Select(id => (Id: id, Actions: expected.GetLegalActions(id)))
                    .Where(seat => seat.Actions.Count > 0)
                    .Select(seat => (seat.Id, Action: policy.Choose(expected.GetSnapshot(seat.Id), seat.Actions)))
                    .ToArray();
                foreach (var (id, action) in cohort) expected.Execute(id, action);
            }
            else
            {
                foreach (var id in seats)
                {
                    var actions = expected.GetLegalActions(id);
                    if (actions.Count > 0)
                        expected.Execute(id, policy.Choose(expected.GetSnapshot(id), actions));
                }
            }
        }
        Assert.Equal(DuckPhase.Finished, expected.GetSnapshot("human").Phase);
        Assert.Equal(JsonSerializer.Serialize(DuckSaves.Capture(expected), SaveJson),
            JsonSerializer.Serialize(files.Read(), SaveJson));
    }
}
