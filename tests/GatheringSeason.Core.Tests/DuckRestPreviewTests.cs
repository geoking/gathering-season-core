using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckRestPreviewTests
{
    private static readonly JsonSerializerOptions SaveJson = new() { IncludeFields = true };

    [Fact]
    public void Preview_requires_a_placed_chip_and_does_not_change_authoritative_state()
    {
        var match = MatchSession.CreateDuck(830, new DuckMatchSettings(4));
        Assert.Null(match.GetSnapshot("human").RestPreview);
        var before = Serialize(DuckSaves.Capture(match));
        Assert.Null(match.GetSnapshot("ai-3").RestPreview);
        Assert.Equal(before, Serialize(DuckSaves.Capture(match)));

        ChooseSunshine(match);
        Explore(match, "human");
        var placed = Serialize(DuckSaves.Capture(match));
        var first = match.GetSnapshot("human").RestPreview;
        var second = match.GetSnapshot("human").RestPreview;
        Assert.NotNull(first);
        Assert.NotSame(first, second);
        Assert.Null(match.GetSnapshot("ai-3").RestPreview);
        Assert.Equal(placed, Serialize(DuckSaves.Capture(match)));
        var resumed = DuckSaves.Restore(DuckSaves.Capture(match));
        Assert.Equal(JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(resumed.GetSnapshot("human").RestPreview));
    }

    [Fact]
    public void Preview_uses_the_occupied_tile_without_looking_at_remaining_private_draw_order()
    {
        var runtime = DuckMatchRuntime.Create(838, new DuckMatchSettings(4));
        PutEventAtCurrentDay(runtime, "shared_supper");
        PutDefinitionFirst(runtime.Player("human"), "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");
        var before = JsonSerializer.Serialize(match.GetSnapshot("human").RestPreview);

        runtime.Player("human").BagPhysicalChipIds.Reverse();
        runtime.Player("ai-3").BagPhysicalChipIds.Reverse();
        var after = JsonSerializer.Serialize(match.GetSnapshot("human").RestPreview);

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Exact_preview_matches_authoritative_Night_under_every_saved_catalogue(int revision)
    {
        var runtime = DuckMatchRuntime.Create(831, rulesRevision: revision);
        PutEventAtCurrentDay(runtime, "still_air");
        PutDefinitionFirst(runtime.Player("human"), "brambles");
        PutDefinitionFirst(runtime.Player("ai"), "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");
        Explore(match, "ai");
        Settle(match, "ai");

        var preview = Assert.IsType<DuckRestPreview>(match.GetSnapshot("human").RestPreview);
        Assert.Equal(preview.StarsMinimum, preview.StarsMaximum);
        Assert.NotEqual(DuckRestBonusStatus.Possible, preview.MostRestedStatus);
        Settle(match, "human");
        var outcome = match.GetSnapshot("human").Players.Single(player => player.Id == "human").LastNightOutcome!;
        AssertMatches(preview, outcome);
    }

    [Fact]
    public void Shared_Supper_is_guaranteed_once_all_ducks_have_placed_Seeds_even_before_they_rest()
    {
        var runtime = DuckMatchRuntime.Create(832, new DuckMatchSettings(4));
        PutEventAtCurrentDay(runtime, "shared_supper");
        foreach (var player in runtime.State.Players) PutDefinitionFirst(player, "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        foreach (var player in runtime.State.Players) Explore(match, player.Id);

        var preview = Assert.IsType<DuckRestPreview>(match.GetSnapshot("human").RestPreview);
        Assert.Equal(DuckRestBonusStatus.Guaranteed, preview.SharedEventStatus);
        Assert.Equal(runtime.Rules.Economy.SharedSupperReward, preview.SharedEventStars);
        Assert.Equal(preview.StarsMinimum, preview.StarsMaximum);
        Assert.Equal(DuckRestBonusStatus.Possible, preview.MostRestedStatus);
        foreach (var player in runtime.State.Players) Settle(match, player.Id);
        AssertMatches(preview, match.GetSnapshot("human").Players[0].LastNightOutcome!);
    }

    [Fact]
    public void Finished_duck_without_Seeds_makes_Shared_Supper_unavailable()
    {
        var runtime = DuckMatchRuntime.Create(833, new DuckMatchSettings(4));
        PutEventAtCurrentDay(runtime, "shared_supper");
        foreach (var player in runtime.State.Players)
            PutDefinitionFirst(player, player.Id == "ai-3" ? "brambles" : "seeds");
        var match = new MatchSession<DuckMatchView>(runtime);
        foreach (var player in runtime.State.Players) Explore(match, player.Id);
        Assert.Equal(DuckRestBonusStatus.Possible,
            match.GetSnapshot("human").RestPreview!.SharedEventStatus);

        Settle(match, "ai-3");
        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(DuckRestBonusStatus.Unavailable, preview.SharedEventStatus);
        foreach (var id in new[] { "human", "ai", "ai-2" }) Settle(match, id);
        AssertMatches(preview, match.GetSnapshot("human").Players[0].LastNightOutcome!);
    }

    [Fact]
    public void Flock_tie_stays_possible_until_opponents_finish_then_becomes_exact()
    {
        var runtime = DuckMatchRuntime.Create(834, new DuckMatchSettings(4));
        PutEventAtCurrentDay(runtime, "still_air");
        foreach (var player in runtime.State.Players)
        {
            if (player.Id is "human" or "ai-3") AddCompanionFirst(runtime, player);
            else PutDefinitionFirst(player, "seeds");
        }
        var match = new MatchSession<DuckMatchView>(runtime);
        foreach (var player in runtime.State.Players) Explore(match, player.Id);
        Assert.Equal(DuckRestBonusStatus.Possible,
            match.GetSnapshot("human").RestPreview!.FlockStatus);

        foreach (var id in new[] { "ai", "ai-2", "ai-3" }) Settle(match, id);
        var exact = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(DuckRestBonusStatus.Guaranteed, exact.FlockStatus);
        Assert.True(exact.FlockStars > 0);
        Assert.Equal(exact.StarsMinimum, exact.StarsMaximum);
        Settle(match, "human");
        AssertMatches(exact, match.GetSnapshot("human").Players[0].LastNightOutcome!);
    }

    [Fact]
    public void A_finished_safe_duck_can_make_Most_Rested_impossible_before_others_finish()
    {
        var runtime = DuckMatchRuntime.Create(839, new DuckMatchSettings(4));
        PutEventAtCurrentDay(runtime, "thick_morning_mist");
        PutDefinitionFirst(runtime.Player("human"), "brambles");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");

        var ai = runtime.Player("ai");
        foreach (var definitionId in new[] { "tailwind_2", "seeds", "seeds" })
        {
            PutDefinitionFirst(ai, definitionId);
            Explore(match, "ai");
        }
        Settle(match, "ai");

        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(4, ai.Position);
        Assert.True(ai.HasFinishedDay);
        Assert.True(runtime.Rules.BoardSpaceAt(ai.Position).Sleep > preview.StarsMaximum);
        Assert.Equal(DuckRestBonusStatus.Unavailable, preview.MostRestedStatus);
        Assert.Equal(preview.DreamTwigsMinimum, preview.DreamTwigsMaximum);
    }

    [Fact]
    public void Worn_out_preview_halves_Stars_and_never_promises_Most_Rested()
    {
        var runtime = DuckMatchRuntime.Create(835);
        PutEventAtCurrentDay(runtime, "still_air");
        var human = runtime.Player("human");
        PutDefinitionFirst(human, "brambles");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");
        human.Exhaustion = human.SafeExhaustionMaximum;
        PutDefinitionFirst(human, "loose_pebbles");
        Explore(match, "human");

        Assert.True(match.GetSnapshot("human").Players[0].IsWornOut);
        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(DuckRestBonusStatus.Unavailable, preview.MostRestedStatus);
        Explore(match, "ai");
        Settle(match, "ai");
        AssertMatches(preview, match.GetSnapshot("human").Players[0].LastNightOutcome!);
    }

    [Fact]
    public void Day_ten_shelter_preview_includes_final_Star_and_Dream_Twig_conversion()
    {
        var runtime = DuckMatchRuntime.Create(836);
        var match = new MatchSession<DuckMatchView>(runtime);
        AdvanceToDayTen(match);
        PutEventAtCurrentDay(runtime, "still_air");
        var human = runtime.Player("human");
        human.PermanentFeatherTrail = 3;
        human.ActiveMostRestedStep = false;
        human.EffectiveStart = 3;
        human.Position = 3;
        PutDefinitionFirst(human, "seeds");

        Explore(match, "human");
        Explore(match, "ai");
        Settle(match, "ai");
        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.True(runtime.Rules.BoardSpaceAt(human.Position).IsShelter);
        Assert.True(preview.FeathersAwarded > 0);
        Assert.Equal(preview.StarsMinimum, preview.StarsMaximum);
        Assert.Equal(DuckRestBonusStatus.Possible, preview.MostRestedStatus);
        Settle(match, "human");
        var outcome = match.GetSnapshot("human").Players[0].LastNightOutcome!;
        Assert.Equal(runtime.Rules.Economy.FinalShelterReward, outcome.FinalShelterSleep);
        AssertMatches(preview, outcome);
    }

    [Fact]
    public void Splash_suppressed_final_Brambles_does_not_reduce_preview_Twigs()
    {
        var runtime = DuckMatchRuntime.Create(837);
        PutEventAtCurrentDay(runtime, "still_air");
        var human = runtime.Player("human");
        PutDefinitionFirst(human, "brambles");
        PutDefinitionFirst(human, "splash");
        var match = new MatchSession<DuckMatchView>(runtime);
        Explore(match, "human");
        Explore(match, "human");
        var preview = match.GetSnapshot("human").RestPreview!;
        Assert.Equal(runtime.Rules.BoardSpaceAt(human.Position).Twigs, preview.TwigsEarnedToday);
        Explore(match, "ai");
        Settle(match, "ai");
        Settle(match, "human");
        var outcome = match.GetSnapshot("human").Players[0].LastNightOutcome!;
        Assert.Equal(0, outcome.BramblesPenalty);
        AssertMatches(preview, outcome);
    }

    [Fact]
    public void Four_duck_preview_bounds_contain_every_resolved_Night_across_the_event_deck()
    {
        var match = MatchSession.CreateDuck(840, new DuckMatchSettings(4));
        var ids = new[] { "human", "ai", "ai-2", "ai-3" };
        for (var day = 1; day <= 10; day++)
        {
            ChooseSunshine(match);
            foreach (var id in ids) Explore(match, id);
            var previews = ids.ToDictionary(id => id,
                id => Assert.IsType<DuckRestPreview>(match.GetSnapshot(id).RestPreview));
            foreach (var id in ids)
                if (match.GetLegalActions(id).Any(action => action.Kind == GameActionKind.Settle))
                    Settle(match, id);

            var resolved = match.GetSnapshot("human");
            foreach (var id in ids)
                AssertMatches(previews[id], resolved.Players.Single(player => player.Id == id).LastNightOutcome!);
            if (day == 10) break;
            foreach (var id in ids) Execute(match, id, GameActionKind.FinishDream);
            Execute(match, "human", GameActionKind.NextDay);
        }
    }

    private static void AssertMatches(DuckRestPreview preview, DuckNightOutcome outcome)
    {
        Assert.Equal(outcome.TotalTwigsEarned, preview.TwigsEarnedToday);
        Assert.Equal(outcome.PrintedTwigs - outcome.BramblesPenalty, preview.NestTwigChangeOnRest);
        Assert.Equal(outcome.FeathersAwarded, preview.FeathersAwarded);
        Assert.InRange(outcome.FrozenSleep, preview.StarsMinimum, preview.StarsMaximum);
        Assert.InRange(outcome.DreamTwigs, preview.DreamTwigsMinimum, preview.DreamTwigsMaximum);
    }

    private static void AdvanceToDayTen(MatchSession<DuckMatchView> match)
    {
        while (match.GetSnapshot("human").Day < 10)
        {
            ChooseSunshine(match);
            foreach (var id in new[] { "human", "ai" }) Explore(match, id);
            foreach (var id in new[] { "human", "ai" })
                if (match.GetLegalActions(id).Any(action => action.Kind == GameActionKind.Settle)) Settle(match, id);
            foreach (var id in new[] { "human", "ai" })
                Execute(match, id, GameActionKind.FinishDream);
            Execute(match, "human", GameActionKind.NextDay);
        }
    }

    private static void ChooseSunshine(MatchSession<DuckMatchView> match)
    {
        foreach (var player in match.GetSnapshot("human").Players)
            if (match.GetLegalActions(player.Id).Any(action => action.Kind == GameActionKind.ChooseEventBenefit))
                Execute(match, player.Id, GameActionKind.ChooseEventBenefit);
    }

    private static void Explore(MatchSession<DuckMatchView> match, string id) => Execute(match, id, GameActionKind.Explore);
    private static void Settle(MatchSession<DuckMatchView> match, string id) => Execute(match, id, GameActionKind.Settle);

    private static void Execute(MatchSession<DuckMatchView> match, string id, GameActionKind kind)
    {
        match.Execute(id, match.GetLegalActions(id).First(action => action.Kind == kind));
    }

    private static void PutEventAtCurrentDay(DuckMatchRuntime runtime, string definitionId)
    {
        var ids = runtime.State.WorldEventDeckDefinitionIds;
        var target = ids.IndexOf(definitionId);
        (ids[runtime.State.CurrentEventIndex], ids[target]) = (ids[target], ids[runtime.State.CurrentEventIndex]);
    }

    private static void PutDefinitionFirst(DuckPlayerState player, string definitionId)
    {
        var id = player.Inventory.First(chip => chip.DefinitionId == definitionId
            && player.BagPhysicalChipIds.Contains(chip.PhysicalChipId)).PhysicalChipId;
        player.BagPhysicalChipIds.Remove(id);
        player.BagPhysicalChipIds.Insert(0, id);
    }

    private static void AddCompanionFirst(DuckMatchRuntime runtime, DuckPlayerState player)
    {
        var chip = new DuckPhysicalChipState(runtime.State.NextPhysicalChipId++, "companion");
        player.Inventory.Add(chip);
        player.BagPhysicalChipIds.Insert(0, chip.PhysicalChipId);
    }

    private static string Serialize(DuckSaveData save) => JsonSerializer.Serialize(save, SaveJson);
}
