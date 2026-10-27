using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.Runtime
{
    public sealed class DuckPendingTailwindView
    {
        internal DuckPendingTailwindView(string definitionId, int fullMovement, int fullPosition, int? shelterPosition)
        { DefinitionId = definitionId; FullMovement = fullMovement; FullPosition = fullPosition; ShelterPosition = shelterPosition; }
        public string DefinitionId { get; }
        public int FullMovement { get; }
        public int FullPosition { get; }
        public int? ShelterPosition { get; }
    }

    public sealed class DuckDrawProbabilityOutcome
    {
        internal DuckDrawProbabilityOutcome(string definitionId, int numerator, int denominator, bool wearsOut)
        { DefinitionId = definitionId; Numerator = numerator; Denominator = denominator; WearsOut = wearsOut; }
        public string DefinitionId { get; }
        public int Numerator { get; }
        public int Denominator { get; }
        public bool WearsOut { get; }
        public double Probability => (double)Numerator / Denominator;
    }

    /// <summary>Exact next-draw distribution from the viewer's lawful composition and Signpost evidence.</summary>
    public sealed class DuckDrawProbabilityView
    {
        internal DuckDrawProbabilityView(IEnumerable<DuckDrawProbabilityOutcome> outcomes)
        { Outcomes = DuckMatchView.Freeze(outcomes); WearOutNumerator = Outcomes.Where(item => item.WearsOut).Sum(item => item.Numerator); Denominator = Outcomes[0].Denominator; }
        public IReadOnlyList<DuckDrawProbabilityOutcome> Outcomes { get; }
        public int WearOutNumerator { get; }
        public int Denominator { get; }
        public double WearOutProbability => (double)WearOutNumerator / Denominator;
    }

    internal static class DuckDrawProbability
    {
        internal static DuckDrawProbabilityView? Calculate(DuckMatchRuntime runtime, DuckPlayerState viewer)
        {
            if (!runtime.GetLegalActions(viewer.Id).Any(action => action.Kind == GameActionKind.Explore)) return null;
            var candidates = viewer.KnownNextPhysicalChipIds.Count > 0 ? viewer.KnownNextPhysicalChipIds.Take(1)
                : viewer.UnorderedNextPhysicalChipIds.Count > 0 ? viewer.UnorderedNextPhysicalChipIds : viewer.BagPhysicalChipIds;
            var definitions = candidates.Select(id => viewer.Inventory.Single(chip => chip.PhysicalChipId == id).DefinitionId).ToArray();
            var before = DuckAdventureHandler.AdventureState(runtime, viewer);
            return new DuckDrawProbabilityView(definitions.GroupBy(id => id).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DuckDrawProbabilityOutcome(group.Key, group.Count(), definitions.Length,
                    DuckAdventureRules.ApplyEncounter(before, runtime.Rules.Encounter(group.Key), runtime.CurrentEvent.EventType, runtime.Rules).WearsOut)));
        }
    }
}
