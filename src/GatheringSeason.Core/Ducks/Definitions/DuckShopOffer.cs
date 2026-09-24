using System;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>One priced Dream shop variant linked to its authoritative encounter definition.</summary>
    public sealed class DuckShopOffer
    {
        public DuckShopOffer(string definitionId, DuckEncounterDefinition encounter, int sleepPrice)
        {
            if (encounter == null) throw new ArgumentNullException(nameof(encounter));
            if (!encounter.IsHelpful) throw new ArgumentException("Obstacle encounters are not sold in the Dream shop.", nameof(encounter));
            if (sleepPrice < 0) throw new ArgumentOutOfRangeException(nameof(sleepPrice));

            DefinitionId = DefinitionIdentity.Require(definitionId, nameof(definitionId));
            Encounter = encounter;
            SleepPrice = sleepPrice;
        }

        public string DefinitionId { get; }
        public DuckEncounterDefinition Encounter { get; }
        public string EncounterDefinitionId => Encounter.DefinitionId;
        public DuckEncounterType ShopType => Encounter.ShopType;
        public int SleepPrice { get; }
        public int Price => SleepPrice;
        public int StarsPrice => SleepPrice;
    }
}
