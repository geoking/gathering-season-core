using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Shared set-aware reward projection, independent of match mutation or policy.</summary>
    internal static class DuckWishRewards
    {
        internal static int ReedsTwigs(DuckRuleDefinitions rules, IEnumerable<DuckEncounterDefinition> placed, bool isShelter) =>
            ReedsTwigs(rules, placed.Sum(chip => chip.TwigYield), isShelter);

        internal static int ReedsTwigs(DuckRuleDefinitions rules, int printedYield, bool isShelter) =>
            rules.IsWishSetTwo && !isShelter ? 0 : printedYield;

        internal static int FlowerStars(DuckRuleDefinitions rules, int flowersPlaced, DuckEncounterType finalType, bool isShelter) =>
            rules.IsWishSetTwo ? (finalType == DuckEncounterType.Wildflowers ? 2 : 0)
                : isShelter ? rules.Economy.CalculateFlowerReward(flowersPlaced) : 0;

        internal static int FlockStars(DuckRuleDefinitions rules, int activeFlock) =>
            activeFlock > 0 ? rules.Economy.CalculateFlockLeaderReward(activeFlock) : 0;
    }
}
