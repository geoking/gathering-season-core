using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Validated immutable definition registry for one Duck rules version.</summary>
    public sealed class DuckRuleDefinitions
    {
        private readonly IReadOnlyDictionary<string, DuckBoardSpace> _boardById;
        private readonly IReadOnlyDictionary<string, DuckEncounterDefinition> _encounterById;
        private readonly IReadOnlyDictionary<string, DuckShopOffer> _shopOfferById;
        private readonly IReadOnlyDictionary<string, DuckWorldEventDefinition> _worldEventById;

        internal DuckRuleDefinitions(
            int rulesRevision,
            int freshAirExhaustionBonus,
            DuckEconomyDefinition economy,
            IEnumerable<DuckBoardSpace> boardSpaces,
            IEnumerable<DuckEncounterDefinition> encounterDefinitions,
            IEnumerable<DuckShopOffer> shopOffers,
            IEnumerable<DuckWorldEventDefinition> worldEvents,
            IEnumerable<DuckEncounterDefinition> openingBag)
        {
            if (rulesRevision < 1) throw new ArgumentOutOfRangeException(nameof(rulesRevision));
            if (freshAirExhaustionBonus < 1) throw new ArgumentOutOfRangeException(nameof(freshAirExhaustionBonus));
            RulesRevision = rulesRevision;
            FreshAirExhaustionBonus = freshAirExhaustionBonus;
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            BoardSpaces = Freeze(boardSpaces, nameof(boardSpaces));
            EncounterDefinitions = Freeze(encounterDefinitions, nameof(encounterDefinitions));
            ShopOffers = Freeze(shopOffers, nameof(shopOffers));
            WorldEvents = Freeze(worldEvents, nameof(worldEvents));
            OpeningBag = Freeze(openingBag, nameof(openingBag));

            _boardById = IndexUnique(BoardSpaces, item => item.DefinitionId, nameof(boardSpaces));
            _encounterById = IndexUnique(EncounterDefinitions, item => item.DefinitionId, nameof(encounterDefinitions));
            _shopOfferById = IndexUnique(ShopOffers, item => item.DefinitionId, nameof(shopOffers));
            _worldEventById = IndexUnique(WorldEvents, item => item.DefinitionId, nameof(worldEvents));

            ValidateCanonicalShape();
        }

        public const int NestPosition = 0;
        public string ProfileId => "gathering-season.duck.v1";
        public int RulesRevision { get; }
        public int FreshAirExhaustionBonus { get; }
        public int FreshAirSafeExhaustionMaximum => 5 + FreshAirExhaustionBonus;
        public int FreshAirGooseSafeExhaustionMaximum => 4 + FreshAirExhaustionBonus;
        public DuckEconomyDefinition Economy { get; }
        public IReadOnlyList<DuckBoardSpace> BoardSpaces { get; }
        public IReadOnlyList<DuckEncounterDefinition> EncounterDefinitions { get; }
        public IReadOnlyList<DuckShopOffer> ShopOffers { get; }
        public IReadOnlyList<DuckWorldEventDefinition> WorldEvents { get; }
        public IReadOnlyList<DuckEncounterDefinition> OpeningBag { get; }

        public DuckBoardSpace BoardSpace(string definitionId) => Find(_boardById, definitionId, "board space");
        public DuckEncounterDefinition Encounter(string definitionId) => Find(_encounterById, definitionId, "encounter");
        public DuckShopOffer ShopOffer(string definitionId) => Find(_shopOfferById, definitionId, "shop offer");
        public DuckWorldEventDefinition WorldEvent(string definitionId) => Find(_worldEventById, definitionId, "World Event");

        public DuckBoardSpace BoardSpaceAt(int space)
        {
            if (space < 1 || space > BoardSpaces.Count) throw new ArgumentOutOfRangeException(nameof(space));
            return BoardSpaces[space - 1];
        }

        private void ValidateCanonicalShape()
        {
            if (BoardSpaces.Count != 43) throw new ArgumentException("Duck v1 requires exactly 43 scorable board spaces.");
            for (var index = 0; index < BoardSpaces.Count; index++)
                if (BoardSpaces[index].Space != index + 1)
                    throw new ArgumentException("Board spaces must be ordered and contiguous from 1 through 43.");

            if (BoardSpaces.Count(space => space.IsShelter) != 8)
                throw new ArgumentException("Duck v1 requires exactly eight shelters.");
            if (EncounterDefinitions.Count != 16)
                throw new ArgumentException("Duck v1 requires exactly 16 encounter variants.");
            if (EncounterDefinitions.Select(item => item.EncounterType).Distinct().Count() != 12)
                throw new ArgumentException("Duck v1 requires seven helpful and five obstacle encounter types.");
            if (EncounterDefinitions.Count(item => item.IsHelpful) != 11 || EncounterDefinitions.Count(item => item.IsObstacle) != 5)
                throw new ArgumentException("Duck v1 requires 11 helpful variants and five obstacle variants.");
            if (ShopOffers.Count != 11)
                throw new ArgumentException("Duck v1 requires exactly 11 shop offers.");
            if (ShopOffers.Any(offer => !_encounterById.TryGetValue(offer.EncounterDefinitionId, out var encounter)
                || !ReferenceEquals(encounter, offer.Encounter)))
                throw new ArgumentException("Every shop offer must reference a registered encounter definition.");
            if (WorldEvents.Count != 10 || WorldEvents.Select(item => item.EventType).Distinct().Count() != 10)
                throw new ArgumentException("Duck v1 requires exactly ten distinct World Events.");
            if (OpeningBag.Count != 13 || OpeningBag.Any(item => !_encounterById.TryGetValue(item.DefinitionId, out var encounter)
                || !ReferenceEquals(encounter, item)))
                throw new ArgumentException("The opening pouch must contain 13 registered encounter definitions.");
        }

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            var items = source.ToArray();
            if (items.Any(item => item is null)) throw new ArgumentException("Definition collections cannot contain null.", parameterName);
            return Array.AsReadOnly(items);
        }

        private static IReadOnlyDictionary<string, T> IndexUnique<T>(
            IEnumerable<T> source,
            Func<T, string> id,
            string parameterName)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in source)
            {
                var definitionId = id(item);
                if (!result.TryAdd(definitionId, item))
                    throw new ArgumentException("Duplicate definition ID: " + definitionId, parameterName);
            }

            return new ReadOnlyDictionary<string, T>(result);
        }

        private static T Find<T>(IReadOnlyDictionary<string, T> definitions, string definitionId, string kind)
        {
            if (definitionId == null) throw new ArgumentNullException(nameof(definitionId));
            if (!definitions.TryGetValue(definitionId, out var definition))
                throw new KeyNotFoundException("Unknown " + kind + " definition ID: " + definitionId);
            return definition;
        }
    }
}
