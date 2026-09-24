using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Reference;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckDisplayReferenceTests
{
    public static TheoryData<string, string[]> CurrentEncounterPowers => new()
    {
        { "seeds", ["A nice snack, as simple as that."] },
        { "tailwind_2", ["2 spaces total", "+1 extra"] },
        { "tailwind_4", ["4 spaces total", "+3 extra"] },
        { "tailwind_6", ["6 spaces total", "+5 extra"] },
        { "signpost", ["2 spaces total", "+1 extra", "privately peek", "that exact chip", "never choose or reorder"] },
        { "splash", ["immediately next placed chip", "movement and Exhaustion still happen", "used even on a Wish", "fades at Day's end if unused", "Pebbles or Brambles for the rest of the route", "cannot clear an older Log slowdown"] },
        { "reeds_1", ["Gather 1 Twig", "usual 1 space", "x1 is the Twig bundle, not movement", "even if you wear out"] },
        { "reeds_2", ["Gather 2 Twigs", "usual 1 space", "x2 is the Twig bundle, not movement", "even if you wear out"] },
        { "reeds_3", ["Gather 3 Twigs", "usual 1 space", "x3 is the Twig bundle, not movement", "even if you wear out"] },
        { "companion", ["first active Companion moves 2", "second 3", "later ones 4", "Mud can lower", "+1 Star total", "ties included", "Worn-out ducks still compete", "before halving Stars"] },
        { "wildflowers", ["+1 Star total", "settle at a shelter", "even when worn out", "do not stack", "before halving Stars"] },
        { "fallen_log", ["move 1 space", "add 1 Exhaustion", "halve the next Wish's total movement after event bonuses", "rounding up to at least 1", "power still works", "slowdown clears", "cannot stack"] },
        { "mud_puddle", ["move 1 space", "add 1 Exhaustion", "lower the active Companion flock count by 1", "minimum of 0", "chips stay owned and placed", "earlier movement stays put"] },
        { "loose_pebbles", ["move 1 space", "add 1 Exhaustion", "lose 1 Star", "final occupied chip", "even after wear-out", "minimum of 0"] },
        { "brambles", ["move 1 space", "add 1 Exhaustion", "lose 1 Twig earned today", "final occupied chip", "even after wear-out", "minimum of 0", "earlier Days are safe"] },
        { "grumpy_goose", ["1 space", "adds 1 Exhaustion", "safe maximum immediately falls to 4", "6 with Fresh Air", "protection leaves it at 5", "7 with Fresh Air", "one Goose joins each pouch on Day 5", "stays", "lowered limit resets at Dawn"] }
    };

    public static TheoryData<DuckWorldEventType, string[]> CurrentEventPowers => new()
    {
        { DuckWorldEventType.RainSoftenedSeeds, ["every placed Seed +1 movement", "2 spaces total", "no Star or Exhaustion"] },
        { DuckWorldEventType.GloriousSunshine, ["publicly chooses", "+2", "7 is safe", "6 after an unprotected Goose", "+2 Stars", "before Pebbles, wear-out halving, Most Rested, and Final Night conversion", "before anyone draws"] },
        { DuckWorldEventType.FriendlyGuide, ["first placed Obstacle", "movement and +1 Exhaustion still happen", "even when the nuisance would do nothing", "Splash used there is spent too", "older Log slowdown remains"] },
        { DuckWorldEventType.PocketOfDriftwood, ["3 different Wish types", "+1 Twig", "kept even if it later wears out", "at most once", "one type each"] },
        { DuckWorldEventType.AllTuckedIn, ["every duck finishes at a shelter", "+1 Star", "different shelters are fine", "worn-out ducks still qualify", "One unsheltered duck"] },
        { DuckWorldEventType.HomeBeforeDark, ["every duck finishes safely", "+1 Star", "Shelters are not required", "one worn-out duck"] },
        { DuckWorldEventType.SharedSupper, ["every duck physically places at least 1 Seed", "+1 Star", "Worn-out ducks still qualify", "before their Stars are halved"] },
        { DuckWorldEventType.StillAir, ["Tailwind 2 moves 1", "Tailwind 4 moves 2", "Tailwind 6 moves 3", "other powers are unchanged", "never halves that Tailwind twice", "next Wish is not a Tailwind, it works normally"] },
        { DuckWorldEventType.ThickMorningMist, ["move 2 spaces total", "preview no chip", "no preview is saved for later"] },
        { DuckWorldEventType.RestlessNight, ["finishes at a shelter", "1 less shelter-related Star", "minimum of 0", "printed shelter reward, Flowers, and Final Night shelter bonus together", "worn-out ducks before halving Stars", "Twigs, Feathers, flock rewards, and other event rewards stay untouched"] }
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Every_encounter_variant_has_reference_text_for_each_supported_revision(int rulesRevision)
    {
        var rules = DuckRules.ForRulesRevision(rulesRevision);

        Assert.Equal(16, rules.EncounterDefinitions.Count);
        foreach (var encounter in rules.EncounterDefinitions)
        {
            var text = DuckDisplayReference.Encounter(encounter, rules);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("System.", text);
        }

        var pebbles = DuckDisplayReference.Encounter(rules.Encounter("loose_pebbles"), rules);
        Assert.Contains(rules.Economy.UsesStars ? "Star" : rules.Economy.CurrencyName, pebbles);
    }

    [Theory]
    [MemberData(nameof(CurrentEncounterPowers))]
    public void Current_encounter_copy_preserves_every_numeric_power_and_condition(
        string definitionId,
        string[] requiredText)
    {
        var text = DuckDisplayReference.Encounter(DuckRules.V1.Encounter(definitionId), DuckRules.V1);

        Assert.All(requiredText, expected => Assert.Contains(expected, text));
    }

    [Fact]
    public void Event_reference_covers_every_enum_value_and_each_revision_catalogue()
    {
        var supportedTypes = new HashSet<DuckWorldEventType>();

        for (var rulesRevision = 1; rulesRevision <= DuckRules.CurrentRulesRevision; rulesRevision++)
        {
            var rules = DuckRules.ForRulesRevision(rulesRevision);
            Assert.Equal(10, rules.WorldEvents.Count);

            foreach (var worldEvent in rules.WorldEvents)
            {
                supportedTypes.Add(worldEvent.EventType);
                Assert.False(string.IsNullOrWhiteSpace(DuckDisplayReference.Event(worldEvent, rules)));
            }
        }

        var enumTypes = Enum.GetValues<DuckWorldEventType>();
        Assert.Equal(11, enumTypes.Length);
        Assert.Equal(enumTypes.Order(), supportedTypes.Order());
        Assert.All(enumTypes, type =>
            Assert.False(string.IsNullOrWhiteSpace(DuckDisplayReference.Event(type, DuckRules.V1))));
    }

    [Theory]
    [MemberData(nameof(CurrentEventPowers))]
    public void Current_event_copy_preserves_every_numeric_power_and_condition(
        DuckWorldEventType eventType,
        string[] requiredText)
    {
        var rules = DuckRules.V1;
        Assert.Contains(rules.WorldEvents, worldEvent => worldEvent.EventType == eventType);

        var text = DuckDisplayReference.Event(eventType, rules);

        Assert.All(requiredText, expected => Assert.Contains(expected, text));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Offer_reference_uses_the_running_catalogue_price_currency_and_power(int rulesRevision)
    {
        var rules = DuckRules.ForRulesRevision(rulesRevision);

        Assert.Equal(11, rules.ShopOffers.Count);
        foreach (var offer in rules.ShopOffers)
        {
            var text = DuckDisplayReference.Offer(offer, rules);
            var price = offer.Price == 0 && rules.Economy.UsesStars
                ? "no Stars"
                : $"{offer.Price} {(offer.Price == 1 && rules.Economy.UsesStars ? "Star" : rules.Economy.CurrencyName)}";

            Assert.StartsWith($"Take this Wish home for {price}. ", text);
            Assert.EndsWith(DuckDisplayReference.Encounter(offer.Encounter, rules), text);
        }
    }

    [Fact]
    public void Economy_dependent_reference_text_follows_the_active_revision()
    {
        var legacy = DuckRules.ForRulesRevision(1);
        var current = DuckRules.V1;

        Assert.Contains("+1 Sleep for one active Companion or +2 Sleep total for two or more",
            DuckDisplayReference.Encounter(legacy.Encounter("companion"), legacy.Economy));
        Assert.Contains("+1 Star total",
            DuckDisplayReference.Encounter(current.Encounter("companion"), current.Economy));
        Assert.Contains("+8 Sleep",
            DuckDisplayReference.Event(DuckWorldEventType.GloriousSunshine, legacy.Economy));
        Assert.Contains("+2 Stars",
            DuckDisplayReference.Event(DuckWorldEventType.GloriousSunshine, current.Economy));
        Assert.Contains("+2 Sleep",
            DuckDisplayReference.Event(DuckWorldEventType.AllTuckedIn, legacy.Economy));
        Assert.Contains("+1 Star",
            DuckDisplayReference.Event(DuckWorldEventType.AllTuckedIn, current.Economy));
        Assert.Contains("preview up to 2 available chips in their fixed draw order",
            DuckDisplayReference.Event(DuckWorldEventType.SunlitSignboards, legacy.Economy));
        Assert.Contains("+2 Sleep at Night for each placed Wildflowers chip",
            DuckDisplayReference.Encounter(legacy.Encounter("wildflowers"), legacy.Economy));
    }

    [Fact]
    public void Sunshine_reference_preserves_revision_five_thresholds()
    {
        var revisionFive = DuckRules.ForRulesRevision(5);
        var revisionSix = DuckRules.ForRulesRevision(6);

        Assert.Contains("+3 to today's safe Exhaustion maximum: 8 is safe, or 7 after an unprotected Goose",
            DuckDisplayReference.Event(DuckWorldEventType.GloriousSunshine, revisionFive));
        Assert.Contains("4, or 7 with Fresh Air; protection leaves it at 5, or 8 with Fresh Air",
            DuckDisplayReference.Encounter(revisionFive.Encounter("grumpy_goose"), revisionFive));
        Assert.Contains("+2 to today's safe Exhaustion maximum: 7 is safe, or 6 after an unprotected Goose",
            DuckDisplayReference.Event(DuckWorldEventType.GloriousSunshine, revisionSix));
    }

    [Fact]
    public void Player_help_identifies_wish_set_and_uses_the_running_economy()
    {
        var legacyHelp = DuckDisplayReference.HowToPlay(DuckRules.ForRulesRevision(1).Economy);
        var currentHelp = DuckDisplayReference.HowToPlay(DuckRules.V1.Economy);
        var legacyGlossary = DuckDisplayReference.Glossary(DuckRules.ForRulesRevision(1));
        var currentGlossary = DuckDisplayReference.Glossary(DuckRules.V1);

        Assert.Equal("Wish Set 1", DuckDisplayReference.WishSetName);
        Assert.Contains("Every chip moves 1 space unless its arrow, power, or today's event says otherwise", currentHelp);
        Assert.Contains("spend tonight's Sleep", legacyHelp);
        Assert.Contains("spend tonight's Stars", currentHelp);
        Assert.Contains("Unspent Stars fade at Dawn", currentHelp);
        Assert.Contains("Sleep: Tonight's Wish-buying budget in this legacy match", legacyGlossary);
        Assert.Contains("each 4 retained Sleep becomes 1 Dream Twig", legacyGlossary);
        Assert.Contains("Stars: Tonight's Wish-buying budget", currentGlossary);
        Assert.Contains("each retained Star becomes 1 Dream Twig", currentGlossary);
        Assert.Contains("Feathers: Permanent head starts, one space each", currentGlossary);
        Assert.Contains("wearing out does not prevent a shelter Feather", currentGlossary);
        Assert.Contains("Most Rested marker: A one-Day head start for every eligible duck tied for Most Rested", currentGlossary);
    }

    [Fact]
    public void Reference_api_rejects_missing_or_unknown_inputs()
    {
        var rules = DuckRules.V1;

        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.HowToPlay(null!));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Glossary((DuckEconomyDefinition)null!));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Encounter(null!, rules.Economy));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Encounter(rules.Encounter("seeds"), (DuckEconomyDefinition)null!));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Event((DuckWorldEventDefinition)null!, rules.Economy));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Event(DuckWorldEventType.AllTuckedIn, (DuckEconomyDefinition)null!));
        Assert.Throws<ArgumentNullException>(() => DuckDisplayReference.Offer(null!, rules.Economy));
        Assert.Throws<ArgumentOutOfRangeException>(() => DuckDisplayReference.Event((DuckWorldEventType)int.MaxValue, rules.Economy));
    }
}
