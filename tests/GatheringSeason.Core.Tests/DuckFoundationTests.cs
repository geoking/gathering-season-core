using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using GatheringSeason.Core.Randomness;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckFoundationTests
{
    [Fact]
    public void Factory_creates_typed_active_day_one_with_only_the_mandatory_first_Explore()
    {
        var match = MatchSession.CreateDuck(seed: 42);
        var view = match.GetSnapshot("human");

        Assert.IsType<MatchSession<DuckMatchView>>(match);
        Assert.Equal("gathering-season.duck.v1", view.ProfileId);
        Assert.Same(DuckMatchSettings.Standard, view.Settings);
        Assert.Equal(10, view.Settings.Days);
        Assert.Equal(1, view.Day);
        Assert.Equal(DuckPhase.Adventure, view.Phase);
        Assert.Equal("human", view.ViewerId);
        Assert.Contains(view.CurrentEvent, DuckRules.V1.WorldEvents);
        Assert.Equal(new[] { "human", "ai" }, view.Players.Select(player => player.Id));
        Assert.Equal(11, view.ShopOffers.Count);
        Assert.Empty(view.History);
        Assert.Empty(view.PublicAwards);
        var action = Assert.Single(match.GetLegalActions("human"));
        Assert.Equal(GameActionKind.Explore, action.Kind);
        Assert.Throws<ArgumentException>(() => match.GetSnapshot("spectator"));
    }

    [Fact]
    public void Every_duck_starts_at_nest_zero_without_a_configurable_Feather_input()
    {
        var settings = new DuckMatchSettings();
        var view = MatchSession.CreateDuck(17, settings).GetSnapshot("human");

        Assert.Same(settings, view.Settings);
        Assert.Equal(10, settings.Days);
        Assert.Equal(0, settings.StartingFeathers);
        Assert.All(view.Players, player =>
        {
            Assert.Equal(0, player.PermanentFeatherTrail);
            Assert.Equal(0, player.EffectiveStart);
            Assert.Equal(0, player.Position);
        });
        var constructor = Assert.Single(typeof(DuckMatchSettings).GetConstructors());
        Assert.Empty(constructor.GetParameters());
    }

    [Fact]
    public void Every_opening_chip_has_unique_physical_identity_and_approved_definition_identity()
    {
        var runtime = DuckMatchRuntime.Create(31);
        var allPhysicalIds = runtime.State.Players
            .SelectMany(player => player.Inventory)
            .Select(chip => chip.PhysicalChipId)
            .ToArray();

        Assert.Equal(26, allPhysicalIds.Length);
        Assert.Equal(26, allPhysicalIds.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 26), allPhysicalIds.OrderBy(id => id));
        Assert.Equal(27, runtime.State.NextPhysicalChipId);

        foreach (var player in runtime.State.Players)
        {
            Assert.Equal(13, player.Inventory.Count);
            Assert.Equal(13, player.BagPhysicalChipIds.Count);
            Assert.Equal(
                player.Inventory.Select(chip => chip.PhysicalChipId).OrderBy(id => id),
                player.BagPhysicalChipIds.OrderBy(id => id));
            Assert.Equal(
                DuckRules.V1.OpeningBag.GroupBy(item => item.DefinitionId)
                    .ToDictionary(group => group.Key, group => group.Count()),
                player.Inventory.GroupBy(item => item.DefinitionId)
                    .ToDictionary(group => group.Key, group => group.Count()));
            Assert.All(player.Inventory, chip => DuckRules.V1.Encounter(chip.DefinitionId));
        }
    }

    [Fact]
    public void Initialization_shuffles_the_event_deck_and_each_bag_once_with_resumable_randomness()
    {
        const int seed = 73;
        var first = DuckMatchRuntime.Create(seed);
        var second = DuckMatchRuntime.Create(seed);
        var expectedRandom = new ResumableRandomSource(seed);

        ConsumeFisherYates(expectedRandom, 10);
        ConsumeFisherYates(expectedRandom, 13);
        ConsumeFisherYates(expectedRandom, 13);
        var expectedState = expectedRandom.CaptureState();

        Assert.Equal(first.State.WorldEventDeckDefinitionIds, second.State.WorldEventDeckDefinitionIds);
        Assert.Equal(
            first.State.Players.Select(player => player.BagPhysicalChipIds.ToArray()),
            second.State.Players.Select(player => player.BagPhysicalChipIds.ToArray()),
            IntSequenceComparer.Instance);
        Assert.Equal(10, first.State.WorldEventDeckDefinitionIds.Distinct().Count());
        Assert.Equal(expectedState.Algorithm, first.State.RandomState.Algorithm);
        Assert.Equal(expectedState.State, first.State.RandomState.State);
        Assert.Equal(expectedState.Increment, first.State.RandomState.Increment);
        Assert.Equal(first.State.WorldEventDeckDefinitionIds[0], first.CurrentEvent.DefinitionId);

        var restored = ResumableRandomSource.Restore(first.State.RandomState);
        var expectedNext = restored.NextInt(1000);
        Assert.Equal(expectedNext, first.NextRandomInt(1000));
    }

    [Fact]
    public void Snapshot_keeps_bag_order_private_but_preserves_exact_known_preview_order()
    {
        var runtime = DuckMatchRuntime.Create(5);
        var viewer = runtime.Player("human");
        viewer.BagPhysicalChipIds.Sort((left, right) => right.CompareTo(left));
        viewer.KnownNextPhysicalChipIds.Add(viewer.BagPhysicalChipIds[3]);
        viewer.KnownNextPhysicalChipIds.Add(viewer.BagPhysicalChipIds[1]);

        var view = runtime.GetSnapshot("human");

        Assert.Equal(view.OwnBag.Select(chip => chip.PhysicalChipId).OrderBy(id => id),
            view.OwnBag.Select(chip => chip.PhysicalChipId));
        Assert.False(viewer.BagPhysicalChipIds.SequenceEqual(view.OwnBag.Select(chip => chip.PhysicalChipId)));
        Assert.Equal(viewer.KnownNextPhysicalChipIds,
            view.KnownNextChips.Select(chip => chip.PhysicalChipId));
        Assert.Equal(viewer.Inventory.Select(chip => chip.PhysicalChipId).OrderBy(id => id),
            view.OwnInventory.Select(chip => chip.PhysicalChipId));
        Assert.Null(typeof(DuckMatchView).GetProperty("WorldEventDeck"));
        Assert.Null(typeof(DuckMatchView).GetProperty("BagDrawOrder"));

        var aiView = runtime.GetSnapshot("ai");
        Assert.DoesNotContain(aiView.OwnInventory,
            chip => view.OwnInventory.Any(humanChip => humanChip.PhysicalChipId == chip.PhysicalChipId));
    }

    [Fact]
    public void Snapshot_is_detached_and_contains_authoritative_day_and_Night_state()
    {
        var runtime = DuckMatchRuntime.Create(11);
        var player = runtime.Player("human");
        var placedId = player.BagPhysicalChipIds[0];
        player.TotalTwigs = 7;
        player.DayReedsTwigs = 2;
        player.DayEventTwigs = 1;
        player.Exhaustion = 3;
        player.ActiveFlock = 2;
        player.SplashProtectionArmed = true;
        player.LogSlowdownPending = true;
        player.GuideProtectionAvailable = true;
        player.FlowersPlaced = 2;
        player.FrozenSleep = 12;
        player.IsSleepFrozen = true;
        player.RemainingSleep = 5;
        player.PendingMostRestedStep = true;
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Reeds);
        player.PlacedChips.Add(new DuckPlacedChipState(placedId, 4, nuisanceSuppressed: true));
        player.PurchasedEncounterDefinitionIds.Add("tailwind_4");
        player.PurchasedShopTypes.Add(DuckEncounterType.Tailwind);
        player.LastNightOutcome = Outcome();
        runtime.AddHistory("human", "A visible test entry.");
        runtime.AddPublicAward(new DuckPublicAwardState(1, "most_rested", new[] { "human" }, 0, 0, 0));

        var beforeMutation = runtime.GetSnapshot("human");
        var playerView = beforeMutation.Players.Single(candidate => candidate.Id == "human");

        Assert.Equal(7, playerView.TotalTwigs);
        Assert.Equal(2, playerView.DayReedsTwigs);
        Assert.Equal(1, playerView.DayEventTwigs);
        Assert.Equal(12, playerView.FrozenSleep);
        Assert.Equal(5, playerView.RemainingSleep);
        Assert.True(playerView.IsSleepFrozen);
        Assert.True(playerView.PlacedChips.Single().NuisanceSuppressed);
        Assert.Equal(DuckEncounterType.Reeds, playerView.PlacedHelpfulTypes.Single());
        Assert.Equal("tailwind_4", playerView.PurchasedEncounterDefinitionIds.Single());
        Assert.Equal(DuckEncounterType.Tailwind, playerView.PurchasedShopTypes.Single());
        Assert.Same(player.LastNightOutcome, playerView.LastNightOutcome);
        Assert.Single(beforeMutation.History);
        Assert.Single(beforeMutation.PublicAwards);

        player.TotalTwigs = 99;
        player.PlacedHelpfulTypes.Clear();
        player.PlacedChips.Clear();
        player.PurchasedEncounterDefinitionIds.Clear();
        runtime.State.History.Clear();
        runtime.State.PublicAwards.Clear();

        Assert.Equal(7, playerView.TotalTwigs);
        Assert.Single(playerView.PlacedHelpfulTypes);
        Assert.Single(playerView.PlacedChips);
        Assert.Single(playerView.PurchasedEncounterDefinitionIds);
        Assert.Single(beforeMutation.History);
        Assert.Single(beforeMutation.PublicAwards);
        Assert.Throws<NotSupportedException>(() => ((IList<DuckPlayerView>)beforeMutation.Players).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DuckPhysicalChipView>)beforeMutation.OwnBag).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DuckHistoryEntry>)beforeMutation.History).Clear());
    }

    [Fact]
    public void Final_Day_commit_foundation_is_typed_and_does_not_reveal_hidden_actions()
    {
        var runtime = DuckMatchRuntime.Create(19);
        runtime.State.Day = 10;
        runtime.State.FinalDayDecisionBeat = 3;
        runtime.State.FinalDayCommits.Add(new DuckFinalDayCommitState(3, "human", GameActionKind.Explore));

        var view = runtime.GetSnapshot("ai");

        Assert.True(view.AwaitingFinalDayDecisions);
        Assert.Equal(3, view.FinalDayDecisionBeat);
        Assert.Null(typeof(DuckMatchView).GetProperty("FinalDayCommits"));
        Assert.Throws<ArgumentException>(() =>
            new DuckFinalDayCommitState(3, "human", GameActionKind.BuyEncounter));
    }

    private static DuckNightOutcome Outcome()
    {
        return new DuckNightOutcome(
            day: 1,
            printedSleep: 6,
            printedTwigs: 1,
            reedsTwigs: 2,
            eventTwigs: 1,
            bramblesPenalty: 0,
            flowerSleep: 4,
            finalShelterSleep: 0,
            restlessNightPenalty: 0,
            collectiveEventSleep: 1,
            gloriousSunshineSleep: 0,
            flockSleep: 1,
            pebblesPenalty: 0,
            sleepBeforeWear: 12,
            frozenSleep: 12,
            totalTwigsEarned: 4,
            feathersAwarded: 1,
            isMostRested: true,
            nextDayTemporaryStep: 1,
            dreamTwigs: 0);
    }

    private static void ConsumeFisherYates(IRandomSource random, int count)
    {
        for (var index = count - 1; index > 0; index--) random.NextInt(index + 1);
    }

    private sealed class IntSequenceComparer : IEqualityComparer<int[]>
    {
        internal static IntSequenceComparer Instance { get; } = new IntSequenceComparer();
        public bool Equals(int[]? left, int[]? right) => left != null && right != null && left.SequenceEqual(right);
        public int GetHashCode(int[] values) => values.Aggregate(17, (hash, value) => hash * 31 + value);
    }
}
