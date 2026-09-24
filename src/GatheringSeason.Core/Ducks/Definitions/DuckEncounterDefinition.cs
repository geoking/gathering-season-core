using System;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Immutable rules data shared by every owned chip of one encounter variant.</summary>
    public sealed class DuckEncounterDefinition
    {
        public DuckEncounterDefinition(
            string definitionId,
            string name,
            DuckEncounterType encounterType,
            int? baseMovement,
            int twigYield,
            int exhaustionValue)
        {
            if (!Enum.IsDefined(typeof(DuckEncounterType), encounterType))
                throw new ArgumentOutOfRangeException(nameof(encounterType));
            if (baseMovement.HasValue && baseMovement.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(baseMovement));
            if (twigYield < 0)
                throw new ArgumentOutOfRangeException(nameof(twigYield));
            if (exhaustionValue < 0)
                throw new ArgumentOutOfRangeException(nameof(exhaustionValue));
            if (encounterType == DuckEncounterType.Companion && baseMovement.HasValue)
                throw new ArgumentException("Companion movement is conditional and must not be stored as base movement.", nameof(baseMovement));
            if (encounterType != DuckEncounterType.Companion && !baseMovement.HasValue)
                throw new ArgumentException("Only Companion has conditional movement in the v1 catalogue.", nameof(baseMovement));
            if (encounterType != DuckEncounterType.Reeds && twigYield != 0)
                throw new ArgumentException("Only Reeds have an intrinsic Twig yield in the v1 catalogue.", nameof(twigYield));

            DefinitionId = DefinitionIdentity.Require(definitionId, nameof(definitionId));
            Name = DefinitionIdentity.RequireName(name, nameof(name));
            EncounterType = encounterType;
            BaseMovement = baseMovement;
            TwigYield = twigYield;
            ExhaustionValue = exhaustionValue;
        }

        public string DefinitionId { get; }
        public string Name { get; }
        public DuckEncounterType EncounterType { get; }
        public int? BaseMovement { get; }
        public int TwigYield { get; }
        public int ExhaustionValue { get; }

        /// <summary>The one-per-type Dream shop identity; variants share this value.</summary>
        public DuckEncounterType ShopType => EncounterType;

        public bool IsHelpful => EncounterType == DuckEncounterType.Seeds
            || EncounterType == DuckEncounterType.Tailwind
            || EncounterType == DuckEncounterType.Signpost
            || EncounterType == DuckEncounterType.Splash
            || EncounterType == DuckEncounterType.Reeds
            || EncounterType == DuckEncounterType.Companion
            || EncounterType == DuckEncounterType.Wildflowers;

        public bool IsWish => IsHelpful;

        public bool IsObstacle => !IsHelpful;
    }
}
