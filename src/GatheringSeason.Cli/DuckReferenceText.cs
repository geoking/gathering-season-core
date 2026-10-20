using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Reference;

namespace GatheringSeason.Cli;

internal static class DuckReferenceText
{
    internal static string Encounter(DuckEncounterDefinition encounter, DuckRuleDefinitions rules) =>
        DuckDisplayReference.Encounter(encounter, rules);

    internal static string Event(DuckWorldEventType type, DuckRuleDefinitions rules) =>
        DuckDisplayReference.WeatherReport(type, rules);

}
