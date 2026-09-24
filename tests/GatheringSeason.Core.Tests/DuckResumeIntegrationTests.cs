using System.Text.Json;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;
using Xunit.Abstractions;

namespace GatheringSeason.Core.Tests;

public sealed class DuckResumeIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public DuckResumeIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly JsonSerializerOptions SaveJson = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    [Fact]
    public void Normal_games_restored_after_every_action_match_uninterrupted_games_exactly()
    {
        var totalActions = 0;
        var sawPreview = false;
        var sawMultiplePurchasesInOneDream = false;
        var sawDayFiveGoose = false;
        var sawPendingFinalBeat = false;
        var totalPurchases = 0;
        var totalPreviewChoices = 0;
        var totalPendingCommitRestores = 0;

        foreach (var seed in new[] { 0, 42, 137 })
        {
            var uninterrupted = MatchSession.CreateDuck(seed);
            var replayed = MatchSession.CreateDuck(seed);
            var uninterruptedPolicy = new DuckNormalPolicy();
            var replayedPolicy = new DuckNormalPolicy();
            var seedActions = 0;
            AssertSavesEqual(uninterrupted, replayed, seed, step: 0, playerId: "initial");

            for (var step = 1; step <= 4000; step++)
            {
                if (uninterrupted.GetSnapshot("human").Phase == DuckPhase.Finished)
                {
                    Assert.Equal(DuckPhase.Finished, replayed.GetSnapshot("human").Phase);
                    Assert.NotNull(uninterrupted.GetSnapshot("human").FinalResult);
                    break;
                }

                var acted = false;
                foreach (var playerId in new[] { "human", "ai" })
                {
                    var uninterruptedView = uninterrupted.GetSnapshot(playerId);
                    var replayedView = replayed.GetSnapshot(playerId);
                    var uninterruptedActions = uninterrupted.GetLegalActions(playerId);
                    var replayedActions = replayed.GetLegalActions(playerId);
                    Assert.Equal(uninterruptedActions.Select(action => action.Id), replayedActions.Select(action => action.Id));
                    if (uninterruptedActions.Count == 0) continue;

                    sawPreview |= uninterruptedView.KnownNextChips.Count > 0;
                    if (uninterruptedView.KnownNextChips.Count > 0) totalPreviewChoices++;
                    var uninterruptedChoice = uninterruptedPolicy.Choose(uninterruptedView, uninterruptedActions);
                    var replayedChoice = replayedPolicy.Choose(replayedView, replayedActions);
                    Assert.Equal(uninterruptedChoice.Id, replayedChoice.Id);
                    Assert.Equal(uninterruptedChoice.Kind, replayedChoice.Kind);
                    Assert.Equal(uninterruptedChoice.DefinitionId, replayedChoice.DefinitionId);
                    if (uninterruptedChoice.Kind == GameActionKind.BuyEncounter) totalPurchases++;

                    uninterrupted.Execute(playerId, uninterruptedChoice);
                    replayed.Execute(playerId, replayedChoice);
                    replayed = JsonRestore(replayed);
                    totalActions++;
                    seedActions++;
                    acted = true;

                    var replayedSave = DuckSaves.Capture(replayed);
                    sawMultiplePurchasesInOneDream |= replayedSave.Players.Any(player =>
                        player.PurchasedEncounterDefinitionIds.Count >= 2);
                    sawDayFiveGoose |= replayedSave.Day >= 5 && replayedSave.Players.All(player =>
                        player.Inventory.Count(chip => chip.DefinitionId == "grumpy_goose") == 1);
                    sawPendingFinalBeat |= replayedSave.FinalDayCommits.Count > 0;
                    if (replayedSave.FinalDayCommits.Count > 0) totalPendingCommitRestores++;
                    AssertSavesEqual(uninterrupted, replayed, seed, step, playerId);
                }

                Assert.True(acted, $"Seed {seed} stalled before final scoring at step {step}.");
                if (step == 4000)
                    throw new InvalidOperationException($"Seed {seed} exceeded the integration action bound.");
            }

            Assert.Equal(DuckPhase.Finished, uninterrupted.GetSnapshot("human").Phase);
            AssertSavesEqual(uninterrupted, replayed, seed, step: 4001, playerId: "finished");
            _output.WriteLine("Seed {0}: {1} actions and JSON restores, final result matched.", seed, seedActions);
        }

        Assert.True(totalActions > 0);
        Assert.True(sawPreview, "The seed matrix should cross at least one private Signpost continuation.");
        Assert.True(sawMultiplePurchasesInOneDream, "The seed matrix should exercise later multi-purchase Dreams.");
        Assert.True(sawDayFiveGoose, "The seed matrix should cross the one-time Day 5 Goose insertion.");
        Assert.True(sawPendingFinalBeat, "The seed matrix should restore a hidden pending Day 10 commitment.");
        _output.WriteLine(
            "Total: {0} actions/restores, {1} purchases, {2} preview choices, {3} pending-commit restores, 3 Day-5 Goose and final-result crossings.",
            totalActions, totalPurchases, totalPreviewChoices, totalPendingCommitRestores);
    }

    private static MatchSession<DuckMatchView> JsonRestore(MatchSession<DuckMatchView> match)
    {
        var json = JsonSerializer.Serialize(DuckSaves.Capture(match), SaveJson);
        var decoded = JsonSerializer.Deserialize<DuckSaveData>(json, SaveJson)
            ?? throw new InvalidOperationException("The public Duck save JSON decoded to null.");
        return DuckSaves.Restore(decoded);
    }

    private static void AssertSavesEqual(
        MatchSession<DuckMatchView> expected,
        MatchSession<DuckMatchView> actual,
        int seed,
        int step,
        string playerId)
    {
        var expectedJson = JsonSerializer.Serialize(DuckSaves.Capture(expected), SaveJson);
        var actualJson = JsonSerializer.Serialize(DuckSaves.Capture(actual), SaveJson);
        Assert.True(expectedJson == actualJson,
            $"Seed {seed} diverged after step {step} for {playerId}.\nExpected: {expectedJson}\nActual: {actualJson}");
    }
}
