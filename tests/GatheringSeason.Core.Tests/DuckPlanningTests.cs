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
        new[] { 7, 8 }.SelectMany(revision => DuckRules.ForRulesRevision(revision).EncounterDefinitions
            .Select(definition => new object[] { revision, definition.DefinitionId }));

    [Theory]
    [MemberData(nameof(EncounterDefinitionIds))]
    public void Runtime_applies_every_shared_encounter_transition_without_changing_draw_ownership(int revision, string definitionId)
    {
        var runtime = DuckMatchRuntime.Create(seed: 733, rulesRevision: revision);
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
            runtime.Rules.Encounter(definitionId),
            DuckWorldEventType.PocketOfDriftwood, runtime.Rules);
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

}
