using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Reference;

namespace GatheringSeason.Cli;

internal static class DuckReferenceText
{
    internal static string Encounter(DuckEncounterDefinition encounter, DuckRuleDefinitions rules) =>
        DuckDisplayReference.Encounter(encounter, rules);

    internal static string Event(DuckWorldEventType type, DuckRuleDefinitions rules) =>
        DuckDisplayReference.Event(type, rules);

    internal static string Encounter(DuckEncounterDefinition encounter, DuckEconomyDefinition economy) =>
        GatheringSeason.Core.Ducks.Reference.DuckDisplayReference.Encounter(encounter, economy);

    internal static string Event(DuckWorldEventType type, DuckEconomyDefinition economy) =>
        GatheringSeason.Core.Ducks.Reference.DuckDisplayReference.Event(type, economy);
}
