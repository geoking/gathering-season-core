using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckMultiplayerTests
{
    private static readonly JsonSerializerOptions SaveJson = new() { IncludeFields = true };

    [Fact]
    public void Two_seat_default_and_explicit_setup_have_identical_seeded_state()
    {
        var original = DuckSaves.Capture(MatchSession.CreateDuck(seed: 720));
        var explicitTwo = DuckSaves.Capture(MatchSession.CreateDuck(720, new DuckMatchSettings(2, "set-1")));

        Assert.Equal(Serialize(original), Serialize(explicitTwo));
        Assert.Equal(new[] { "human", "ai" }, original.Players.Select(player => player.Id));
        Assert.Equal(new[] { "Human", "AI" }, original.Players.Select(player => player.Name));
        Assert.Equal(2, original.Settings.PlayerCount);
        Assert.Equal("set-1", original.Settings.WishSetId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_seat_completes_ten_days_and_restores_the_final_standings(int count)
    {
        var match = MatchSession.CreateDuck(721, new DuckMatchSettings(count));
        Assert.Equal(new[] { "human", "ai", "ai-2", "ai-3" }.Take(count),
            match.GetSnapshot("human").Players.Select(player => player.Id));
        Assert.Equal(new[] { "Human", "AI", "AI 2", "AI 3" }.Take(count),
            match.GetSnapshot("human").Players.Select(player => player.Name));

        PlayToDay(match, 10);
        FinishAdventure(match);
        var finished = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(finished);

        Assert.Equal(DuckPhase.Finished, restored.GetSnapshot("human").Phase);
        Assert.Equal(count, restored.GetSnapshot("human").FinalResult!.Standings.Count);
        Assert.NotEmpty(restored.GetSnapshot("human").FinalResult!.WinnerIds);
        Assert.Equal(Serialize(finished), Serialize(DuckSaves.Capture(restored)));
        Assert.All(finished.Players, player => Assert.Empty(restored.GetLegalActions(player.Id)));
    }

    [Fact]
    public void Partial_four_seat_final_beat_restores_without_exposing_or_replaying_commits()
    {
        var match = MatchSession.CreateDuck(722, new DuckMatchSettings(4));
        PlayToDay(match, 10);
        ChooseSunshineForAll(match);
        Execute(match, "human", GameActionKind.Explore);
        Execute(match, "ai-2", GameActionKind.Explore);
        var pending = DuckSaves.Capture(match);
        var restored = DuckSaves.Restore(pending);

        Assert.Equal(new[] { "human", "ai-2" }, pending.FinalDayCommits.Select(commit => commit.PlayerId));
        Assert.All(pending.Players, player =>
            Assert.True(restored.GetSnapshot(player.Id).AwaitingFinalDayDecisions));
        Assert.Empty(restored.GetLegalActions("human"));
        Assert.Empty(restored.GetLegalActions("ai-2"));
        Assert.Equal(Serialize(pending), Serialize(DuckSaves.Capture(restored)));

        foreach (var playerId in new[] { "ai", "ai-3" })
        {
            Execute(match, playerId, GameActionKind.Explore);
            Execute(restored, playerId, GameActionKind.Explore);
        }
        Assert.Empty(DuckSaves.Capture(restored).FinalDayCommits);
        Assert.Equal(Serialize(DuckSaves.Capture(match)), Serialize(DuckSaves.Capture(restored)));
    }

    [Fact]
    public void Four_seat_sunshine_and_shopping_restore_with_each_players_choices()
    {
        var runtime = DuckMatchRuntime.Create(723, new DuckMatchSettings(4));
        PutEventFirst(runtime, "glorious_sunshine");
        foreach (var player in runtime.State.Players) PutDefinitionFirst(player, "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        foreach (var player in runtime.State.Players)
            Execute(match, player.Id, GameActionKind.ChooseEventBenefit,
                player.Id == "ai-3" ? "fresh-air" : "warm-dreams");
        FinishAdventure(match);
        var night = DuckSaves.Capture(match);
        Assert.Equal(4, night.Players.Count);
        Assert.False(night.Players.Single(player => player.Id == "ai-3").GloriousSunshineBenefit
            == DuckGloriousSunshineBenefit.WarmDreams);
        var restored = DuckSaves.Restore(night);
        Assert.Equal(Serialize(night), Serialize(DuckSaves.Capture(restored)));

        foreach (var playerId in new[] { "human", "ai", "ai-2" })
        {
            Execute(match, playerId, GameActionKind.BuyEncounter, "tailwind_2");
            Execute(restored, playerId, GameActionKind.BuyEncounter, "tailwind_2");
        }
        Assert.Equal(Serialize(DuckSaves.Capture(match)), Serialize(DuckSaves.Capture(restored)));
        Assert.All(DuckSaves.Capture(restored).Players.Take(3), player =>
            Assert.Contains("tailwind_2", player.PurchasedEncounterDefinitionIds));
    }

    [Fact]
    public void Legacy_setup_defaults_resolve_set_one_without_changing_the_revision_catalogue()
    {
        var runtime = DuckMatchRuntime.Create(724, rulesRevision: 1);
        var save = DuckSaves.Capture(new MatchSession<DuckMatchView>(runtime));
        save.Settings.PlayerCount = 0;
        save.Settings.WishSetId = null;

        var restored = DuckSaves.Restore(save);
        var view = restored.GetSnapshot("human");
        Assert.Equal(1, view.RulesRevision);
        Assert.Equal(2, view.Settings.PlayerCount);
        Assert.Equal("set-1", view.Settings.WishSetId);
        Assert.Equal(5, view.ShopOffers.Single(offer => offer.DefinitionId == "tailwind_2").Price);
        Assert.Equal(2, DuckSaves.Capture(restored).Settings.PlayerCount);
    }

    [Fact]
    public void Unsupported_setup_and_malformed_seat_records_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuckMatchSettings(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuckMatchSettings(5));
        Assert.Throws<ArgumentException>(() => new DuckMatchSettings(3, "set-2"));
        Assert.Throws<ArgumentException>(() => DuckMatchRuntime.Create(1, new DuckMatchSettings(3), rulesRevision: 6));

        Invalid(save => save.Settings.PlayerCount = 1);
        Invalid(save => save.Settings.PlayerCount = 5);
        Invalid(save => save.Settings.PlayerCount = 2);
        Invalid(save => save.Settings.WishSetId = "set-2");
        Invalid(save => save.Settings.WishSetId = " ");
        Invalid(save => save.RulesVersion = 6);
        Invalid(save => save.Players[2].Id = "ai");
        Invalid(save => save.Players.Reverse());
        Invalid(save => save.CommandRevisions.RemoveAt(2));
        Invalid(save => save.History.Add(new DuckHistorySaveData { Day = 1, ActorId = "ai-4", Message = "bad" }));
        Invalid(save => save.PublicAwards.Add(new DuckPublicAwardSaveData
        {
            Day = 1, DefinitionId = "most_rested", PlayerIds = new() { "human", "ai-4" }
        }));

        var finished = MatchSession.CreateDuck(725, new DuckMatchSettings(4));
        PlayToDay(finished, 10);
        FinishAdventure(finished);
        var badStanding = DuckSaves.Capture(finished);
        badStanding.FinalResult!.Standings[3].PlayerId = badStanding.FinalResult.Standings[0].PlayerId;
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(badStanding));
    }

    [Fact]
    public void Four_seat_final_commit_validation_rejects_missing_ids_duplicates_and_complete_cohorts()
    {
        var match = MatchSession.CreateDuck(730, new DuckMatchSettings(4));
        PlayToDay(match, 10);
        ChooseSunshineForAll(match);
        Execute(match, "human", GameActionKind.Explore);
        var pending = DuckSaves.Capture(match);

        var unknown = Copy(pending);
        unknown.FinalDayCommits[0].PlayerId = "ai-4";
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(unknown));

        var duplicate = Copy(pending);
        duplicate.FinalDayCommits.Add(new DuckFinalDayCommitSaveData
        {
            Beat = duplicate.FinalDayDecisionBeat,
            PlayerId = "human",
            ActionKind = GameActionKind.Explore
        });
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(duplicate));

        var complete = Copy(pending);
        foreach (var id in new[] { "ai", "ai-2", "ai-3" })
            complete.FinalDayCommits.Add(new DuckFinalDayCommitSaveData
            {
                Beat = complete.FinalDayDecisionBeat,
                PlayerId = id,
                ActionKind = GameActionKind.Explore
            });
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(complete));
    }

    [Fact]
    public void Four_seat_observations_reveal_only_the_viewers_private_bag_and_preview()
    {
        var runtime = DuckMatchRuntime.Create(726, new DuckMatchSettings(4));
        PutDefinitionFirst(runtime.Player("ai-2"), "signpost");
        var match = new MatchSession<DuckMatchView>(runtime);
        ChooseSunshineForAll(match);
        Execute(match, "ai-2", GameActionKind.Explore);

        var own = match.GetSnapshot("ai-2");
        var save = DuckSaves.Capture(match);
        Assert.Single(own.KnownNextChips);
        Assert.Equal(own.Players.Single(player => player.Id == "ai-2").BagCount, own.OwnBag.Count);
        foreach (var viewerId in new[] { "human", "ai", "ai-3" })
        {
            var other = match.GetSnapshot(viewerId);
            var ownIds = save.Players.Single(player => player.Id == viewerId).Inventory
                .Select(chip => chip.PhysicalChipId).ToHashSet();
            Assert.Empty(other.KnownNextChips);
            Assert.Equal(other.Players.Single(player => player.Id == viewerId).BagCount, other.OwnBag.Count);
            Assert.All(other.OwnBag, chip => Assert.Contains(chip.PhysicalChipId, ownIds));
            Assert.All(other.OwnInventory, chip => Assert.Contains(chip.PhysicalChipId, ownIds));
            Assert.DoesNotContain(own.KnownNextChips[0].PhysicalChipId,
                other.OwnInventory.Select(chip => chip.PhysicalChipId));
        }
    }

    [Fact]
    public void Shared_supper_and_most_rested_award_all_four_tied_ducks()
    {
        var runtime = DuckMatchRuntime.Create(727, new DuckMatchSettings(4));
        PutEventFirst(runtime, "shared_supper");
        foreach (var player in runtime.State.Players) PutDefinitionFirst(player, "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishAdventure(match);
        var save = DuckSaves.Capture(match);

        Assert.Equal(DuckPhase.Night, save.Phase);
        Assert.All(save.Players, player =>
        {
            Assert.Equal(runtime.Rules.Economy.SharedSupperReward, player.LastNightOutcome!.CollectiveEventSleep);
            Assert.True(player.LastNightOutcome.IsMostRested);
        });
        Assert.Equal(4, save.PublicAwards.Count(award => award.DefinitionId == "most_rested"));
    }

    [Fact]
    public void Flock_contest_rewards_both_leaders_across_all_four_seats()
    {
        var runtime = DuckMatchRuntime.Create(729, new DuckMatchSettings(4));
        foreach (var player in runtime.State.Players)
        {
            if (player.Id is "human" or "ai-3")
            {
                var chip = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, "companion");
                player.Inventory.Add(chip);
                player.BagPhysicalChipIds.Insert(0, chip.PhysicalChipId);
            }
            else PutDefinitionFirst(player, "seeds");
        }
        var match = new MatchSession<DuckMatchView>(runtime);
        FinishAdventure(match);
        var players = DuckSaves.Capture(match).Players;

        Assert.True(players.Single(player => player.Id == "human").LastNightOutcome!.FlockSleep > 0);
        Assert.True(players.Single(player => player.Id == "ai-3").LastNightOutcome!.FlockSleep > 0);
        Assert.Equal(0, players.Single(player => player.Id == "ai").LastNightOutcome!.FlockSleep);
        Assert.Equal(0, players.Single(player => player.Id == "ai-2").LastNightOutcome!.FlockSleep);
    }

    private static void Invalid(Action<DuckSaveData> mutate)
    {
        var save = DuckSaves.Capture(MatchSession.CreateDuck(728, new DuckMatchSettings(4)));
        mutate(save);
        Assert.Throws<DuckSaveValidationException>(() => DuckSaves.Restore(save));
    }

    private static void PlayToDay(MatchSession<DuckMatchView> match, int day)
    {
        while (match.GetSnapshot("human").Day < day)
        {
            FinishAdventure(match);
            foreach (var player in match.GetSnapshot("human").Players)
                Execute(match, player.Id, GameActionKind.FinishDream);
            Execute(match, "human", GameActionKind.NextDay);
        }
    }

    private static void FinishAdventure(MatchSession<DuckMatchView> match)
    {
        ChooseSunshineForAll(match);
        var ids = match.GetSnapshot("human").Players.Select(player => player.Id).ToArray();
        foreach (var id in ids)
            if (match.GetLegalActions(id).Any(action => action.Kind == GameActionKind.Explore))
                Execute(match, id, GameActionKind.Explore);
        while (match.GetSnapshot("human").Phase == DuckPhase.Adventure)
            foreach (var id in ids)
                if (match.GetLegalActions(id).Any(action => action.Kind == GameActionKind.Settle))
                    Execute(match, id, GameActionKind.Settle);
    }

    private static void ChooseSunshineForAll(MatchSession<DuckMatchView> match)
    {
        foreach (var player in match.GetSnapshot("human").Players)
            if (match.GetLegalActions(player.Id).Any(action => action.Kind == GameActionKind.ChooseEventBenefit))
                Execute(match, player.Id, GameActionKind.ChooseEventBenefit, "warm-dreams");
    }

    private static void Execute(MatchSession<DuckMatchView> match, string playerId, GameActionKind kind,
        string? actionFragment = null)
    {
        var action = match.GetLegalActions(playerId).Single(action => action.Kind == kind
            && (actionFragment == null || action.Id.Contains(actionFragment, StringComparison.Ordinal)
                || action.DefinitionId == actionFragment));
        match.Execute(playerId, action);
    }

    private static void PutEventFirst(DuckMatchRuntime runtime, string definitionId)
    {
        var ids = runtime.State.WorldEventDeckDefinitionIds;
        var index = ids.IndexOf(definitionId);
        (ids[0], ids[index]) = (ids[index], ids[0]);
    }

    private static void PutDefinitionFirst(DuckPlayerState player, string definitionId)
    {
        var id = player.Inventory.First(chip => chip.DefinitionId == definitionId).PhysicalChipId;
        player.BagPhysicalChipIds.Remove(id);
        player.BagPhysicalChipIds.Insert(0, id);
    }

    private static string Serialize(DuckSaveData save) => JsonSerializer.Serialize(save, SaveJson);

    private static DuckSaveData Copy(DuckSaveData save) =>
        JsonSerializer.Deserialize<DuckSaveData>(Serialize(save), SaveJson)!;
}
