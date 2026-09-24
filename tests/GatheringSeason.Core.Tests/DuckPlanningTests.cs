using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckPlanningTests
{
    public static IEnumerable<object[]> EncounterDefinitionIds =>
        DuckRules.V1.EncounterDefinitions.Select(definition => new object[] { definition.DefinitionId });

    [Theory]
    [MemberData(nameof(EncounterDefinitionIds))]
    public void Runtime_applies_every_shared_encounter_transition_without_changing_draw_ownership(string definitionId)
    {
        var runtime = DuckMatchRuntime.Create(seed: 733);
        runtime.State.WorldEventDeckDefinitionIds[runtime.State.CurrentEventIndex] = "a_pocket_of_driftwood";
        var player = runtime.Player("human");
        player.Inventory.Clear();
        player.BagPhysicalChipIds.Clear();
        player.KnownNextPhysicalChipIds.Clear();
        player.PlacedChips.Clear();
        player.PlacedHelpfulTypes.Clear();
        player.Position = 3;
        player.Exhaustion = 1;
        player.SafeExhaustionMaximum = 5;
        player.ActiveFlock = 1;
        player.SplashProtectionArmed = true;
        player.LogSlowdownPending = true;
        player.GuideProtectionAvailable = true;
        player.PocketDriftwoodAwarded = false;
        player.FlowersPlaced = 1;
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Seeds);
        player.PlacedHelpfulTypes.Add(DuckEncounterType.Tailwind);

        var placedId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(placedId, "seeds"));
        player.PlacedChips.Add(new DuckPlacedChipState(placedId, player.Position, nuisanceSuppressed: false));
        var candidateId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(candidateId, definitionId));
        player.BagPhysicalChipIds.Add(candidateId);
        player.KnownNextPhysicalChipIds.Add(candidateId);
        var backupId = runtime.State.NextPhysicalChipId++;
        player.Inventory.Add(new DuckPhysicalChipState(backupId, "seeds"));
        player.BagPhysicalChipIds.Add(backupId);

        var before = new DuckAdventureState(
            player.Position,
            player.Exhaustion,
            player.SafeExhaustionMaximum,
            player.ActiveFlock,
            player.SplashProtectionArmed,
            player.LogSlowdownPending,
            player.GuideProtectionAvailable,
            player.PocketDriftwoodAwarded,
            player.FlowersPlaced,
            DuckAdventureRules.HelpfulTypes(player.PlacedHelpfulTypes));
        var expected = DuckAdventureRules.ApplyEncounter(
            before,
            DuckRules.V1.Encounter(definitionId),
            DuckWorldEventType.PocketOfDriftwood);
        var reedsBefore = player.DayReedsTwigs;
        var eventBefore = player.DayEventTwigs;
        var twigsBefore = player.TotalTwigs;
        var match = new MatchSession<DuckMatchView>(runtime);

        match.Execute("human", match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.Explore));

        Assert.Equal(expected.State.Position, player.Position);
        Assert.Equal(expected.State.Exhaustion, player.Exhaustion);
        Assert.Equal(expected.State.SafeExhaustionMaximum, player.SafeExhaustionMaximum);
        Assert.Equal(expected.State.ActiveFlock, player.ActiveFlock);
        Assert.Equal(expected.State.SplashProtectionArmed, player.SplashProtectionArmed);
        Assert.Equal(expected.State.LogSlowdownPending, player.LogSlowdownPending);
        Assert.Equal(expected.State.GuideProtectionAvailable, player.GuideProtectionAvailable);
        Assert.Equal(expected.State.PocketDriftwoodAwarded, player.PocketDriftwoodAwarded);
        Assert.Equal(expected.State.FlowersPlaced, player.FlowersPlaced);
        Assert.Equal(reedsBefore + expected.ReedsTwigsAwarded, player.DayReedsTwigs);
        Assert.Equal(eventBefore + expected.EventTwigsAwarded, player.DayEventTwigs);
        Assert.Equal(twigsBefore + expected.ReedsTwigsAwarded + expected.EventTwigsAwarded, player.TotalTwigs);
        Assert.Equal(candidateId, player.PlacedChips.Last().PhysicalChipId);
        Assert.Equal(expected.Movement, player.Position - before.Position);
        Assert.Equal(expected.NuisanceSuppressed, player.PlacedChips.Last().NuisanceSuppressed);
        Assert.Single(player.BagPhysicalChipIds);
        Assert.Equal(backupId, player.BagPhysicalChipIds[0]);
    }

    [Fact]
    public void Shared_transition_keeps_the_canonical_still_air_log_and_companion_ordering()
    {
        var slowed = State(position: 3, logSlowdownPending: true);
        var tailwind = DuckAdventureRules.ApplyEncounter(
            slowed,
            DuckRules.V1.Encounter("tailwind_4"),
            DuckWorldEventType.StillAir);

        Assert.Equal(2, tailwind.Movement);
        Assert.Equal(5, tailwind.State.Position);
        Assert.False(tailwind.State.LogSlowdownPending);

        var companion = DuckAdventureRules.ApplyEncounter(
            State(position: 3, activeFlock: 1),
            DuckRules.V1.Encounter("companion"),
            DuckWorldEventType.HomeBeforeDark);
        Assert.Equal(2, companion.State.ActiveFlock);
        Assert.Equal(3, companion.Movement);
    }

    [Fact]
    public void Shared_transition_keeps_suppression_exhaustion_goose_and_driftwood_rules_exact()
    {
        var suppressedLog = DuckAdventureRules.ApplyEncounter(
            State(position: 3, exhaustion: 4, splashProtectionArmed: true),
            DuckRules.V1.Encounter("fallen_log"),
            DuckWorldEventType.HomeBeforeDark);
        Assert.True(suppressedLog.NuisanceSuppressed);
        Assert.Equal(5, suppressedLog.State.Exhaustion);
        Assert.False(suppressedLog.State.LogSlowdownPending);
        Assert.False(suppressedLog.WearsOut);

        var goose = DuckAdventureRules.ApplyEncounter(
            State(position: 3, exhaustion: 4),
            DuckRules.V1.Encounter("grumpy_goose"),
            DuckWorldEventType.HomeBeforeDark);
        Assert.Equal(4, goose.State.SafeExhaustionMaximum);
        Assert.True(goose.WearsOut);

        var driftwood = DuckAdventureRules.ApplyEncounter(
            State(
                position: 3,
                helpfulTypes: new[] { DuckEncounterType.Seeds, DuckEncounterType.Tailwind }),
            DuckRules.V1.Encounter("wildflowers"),
            DuckWorldEventType.PocketOfDriftwood);
        Assert.True(driftwood.State.PocketDriftwoodAwarded);
        Assert.Equal(1, driftwood.EventTwigsAwarded);
    }

    private static DuckAdventureState State(
        int position,
        int exhaustion = 0,
        int activeFlock = 0,
        bool splashProtectionArmed = false,
        bool logSlowdownPending = false,
        DuckEncounterType[]? helpfulTypes = null) =>
        new(
            position,
            exhaustion,
            safeExhaustionMaximum: 5,
            activeFlock,
            splashProtectionArmed,
            logSlowdownPending,
            guideProtectionAvailable: false,
            pocketDriftwoodAwarded: false,
            flowersPlaced: 0,
            DuckAdventureRules.HelpfulTypes(helpfulTypes ?? new[] { DuckEncounterType.Seeds }));
}
