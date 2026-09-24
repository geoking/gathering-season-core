using System;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Reference
{
    /// <summary>
    /// Player-facing, display-only rules text for a Duck match catalogue.
    /// This type describes authoritative definitions but never decides legality or changes match state.
    /// </summary>
    public static class DuckDisplayReference
    {
        public const string WishSetName = "Wish Set 1";

        public static string HowToPlay(DuckEconomyDefinition economy)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            return "By Day, draw Wishes and Obstacles. Every chip moves 1 space unless its arrow, power, or today's event says otherwise; resolve it, then draw again or settle before Exhaustion wears you out.\n"
                + $"By Night, spend tonight's {economy.CurrencyName} on new Wishes for tomorrow's pouch. Unspent {economy.CurrencyName} fade at Dawn.\n"
                + $"Gather Twigs across ten Days. Most Twigs wins; ties compare Final Night's retained {economy.CurrencyName}, then share the victory if still tied.";
        }

        public static string Glossary(DuckEconomyDefinition economy)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            var currency = economy.CurrencyName;
            var currencyExplanation = economy.UsesStars
                ? "Stars: Tonight's Wish-buying budget. Unspent Stars fade at Dawn; on Final Night, each retained Star becomes 1 Dream Twig."
                : $"{currency}: Tonight's Wish-buying budget in this legacy match. Unspent {currency} fades at Dawn; on Final Night, each {economy.FinalRewardPerDreamTwig} retained {currency} becomes 1 Dream Twig.";

            return "Wishes: Helpful chips for the journey. Draw them by Day and bring new ones home from the shop at Night.\n"
                + "Obstacles and Exhaustion: White chips move 1 space and add 1 Exhaustion. Protection hushes only their nuisance; movement and Exhaustion still happen.\n"
                + currencyExplanation + "\n"
                + "Nest Twigs: Your victory score. The Twig number at your resting space is the whole route total; Reeds and event Twigs are added to your nest.\n"
                + "Feathers: Permanent head starts, one space each and never spent. Safe shelters and Dawn Delivery can award them.\n"
                + "Most Rested marker: A one-Day head start for every eligible duck tied for Most Rested. On Final Night, it becomes 1 Dream Twig instead.";
        }

        public static string Glossary(DuckRuleDefinitions rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return rules.RulesRevision < 5
                ? Glossary(rules.Economy)
                : Glossary(rules.Economy).Replace(
                    "Safe shelters and Dawn Delivery can award them.",
                    "Shelters and Dawn Delivery can award them; wearing out does not prevent a shelter Feather.");
        }

        public static string Encounter(DuckEncounterDefinition encounter, DuckEconomyDefinition economy)
        {
            return Encounter(encounter, economy, wornOutKeepsNightRewards: false, freshAirExhaustionBonus: 3);
        }

        public static string Encounter(DuckEncounterDefinition encounter, DuckRuleDefinitions rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return Encounter(encounter, rules.Economy, rules.RulesRevision >= 5, rules.FreshAirExhaustionBonus);
        }

        private static string Encounter(
            DuckEncounterDefinition encounter,
            DuckEconomyDefinition economy,
            bool wornOutKeepsNightRewards,
            int freshAirExhaustionBonus)
        {
            if (encounter == null) throw new ArgumentNullException(nameof(encounter));
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            return encounter.EncounterType switch
            {
                DuckEncounterType.Seeds => "A nice snack, as simple as that.",
                DuckEncounterType.Tailwind => $"A gentle breeze carries you {encounter.BaseMovement} spaces total (+{encounter.BaseMovement - 1} extra).",
                DuckEncounterType.Signpost => "A friendly sign carries you 2 spaces total (+1 extra), then lets you privately peek at the next chip. Settle and return it, or continue with that exact chip; never choose or reorder.",
                DuckEncounterType.Splash => "A refreshing splash protects only the immediately next placed chip's nuisance; that chip's movement and Exhaustion still happen. It is used even on a Wish, fades at Day's end if unused, marks Pebbles or Brambles for the rest of the route, and cannot clear an older Log slowdown.",
                DuckEncounterType.Reeds => $"Gather {encounter.TwigYield} Twig{(encounter.TwigYield == 1 ? "" : "s")} for the nest, kept even if you wear out. Reeds still move the usual 1 space; x{encounter.TwigYield} is the Twig bundle, not movement.",
                DuckEncounterType.Companion => $"Company quickens your waddle: the first active Companion moves 2 spaces, the second 3, and later ones 4; Mud can lower the flock count. The largest {(wornOutKeepsNightRewards ? "" : "safe ")}positive flock gains {(economy.UsesStars ? "+1 Star total" : "+1 Sleep for one active Companion or +2 Sleep total for two or more")}, with ties included.{(wornOutKeepsNightRewards ? " Worn-out ducks still compete and add the reward before halving Stars." : "")}",
                DuckEncounterType.Wildflowers => economy.UsesStars
                    ? wornOutKeepsNightRewards
                        ? "A shelter bouquet is worth +1 Star total at Night if you settle at a shelter after placing any Wildflowers, even when worn out. The extra Flowers do not stack the bonus; worn-out ducks add it before halving Stars."
                        : "A shelter bouquet is worth +1 Star total at Night if you settle there safely after placing any Wildflowers. The extra Flowers do not stack the bonus."
                    : "A shelter bouquet grants +2 Sleep at Night for each placed Wildflowers chip, but only if you settle safely at a shelter.",
                DuckEncounterType.FallenLog => "Hop over it: move 1 space and add 1 Exhaustion. Unless protected, halve the next Wish's total movement after event bonuses, rounding up to at least 1; its power still works, the slowdown clears, and another Log cannot stack it.",
                DuckEncounterType.MudPuddle => "Squelch through: move 1 space and add 1 Exhaustion. Unless protected, lower the active Companion flock count by 1, to a minimum of 0; Companion chips stay owned and placed, and earlier movement stays put.",
                DuckEncounterType.LoosePebbles => $"Skitter onward: move 1 space and add 1 Exhaustion. Unless protected, lose 1 {SingularCurrency(economy)} if this is your final occupied chip, even after wear-out, to a minimum of 0.",
                DuckEncounterType.Brambles => "Push through: move 1 space and add 1 Exhaustion. Unless protected, lose 1 Twig earned today if this is your final occupied chip, even after wear-out, to a minimum of 0; Twigs from earlier Days are safe.",
                DuckEncounterType.GrumpyGoose => $"The Goose hustles you 1 space and adds 1 Exhaustion. Unless protected, today's safe maximum immediately falls to 4, or {4 + freshAirExhaustionBonus} with Fresh Air; protection leaves it at 5, or {5 + freshAirExhaustionBonus} with Fresh Air. Exactly one Goose joins each pouch on Day 5 and stays, while the lowered limit resets at Dawn.",
                _ => throw new ArgumentOutOfRangeException(nameof(encounter))
            };
        }

        public static string Event(DuckWorldEventType type, DuckEconomyDefinition economy)
        {
            return Event(type, economy, wornOutKeepsNightRewards: false, freshAirExhaustionBonus: 3);
        }

        public static string Event(DuckWorldEventType type, DuckRuleDefinitions rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return Event(type, rules.Economy, rules.RulesRevision >= 5, rules.FreshAirExhaustionBonus);
        }

        private static string Event(
            DuckWorldEventType type,
            DuckEconomyDefinition economy,
            bool wornOutKeepsNightRewards,
            int freshAirExhaustionBonus)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            return type switch
            {
                DuckWorldEventType.RainSoftenedSeeds => "Soft earth gives every placed Seed +1 movement today, so each Seed moves 2 spaces total. It adds no Star or Exhaustion.",
                DuckWorldEventType.SunlitSignboards => "Sunlight makes every placed Signpost preview up to 2 available chips in their fixed draw order today.",
                DuckWorldEventType.FriendlyGuide => "A kindly guide protects each duck's first placed Obstacle from its nuisance; movement and +1 Exhaustion still happen. It is used even when the nuisance would do nothing, Splash used there is spent too, and an older Log slowdown remains.",
                DuckWorldEventType.PocketOfDriftwood => "The first time each duck places 3 different Wish types today, it gathers +1 Twig, kept even if it later wears out; award this at most once. Tailwind and Reeds variants still count as one type each.",
                DuckWorldEventType.AllTuckedIn => wornOutKeepsNightRewards
                    ? $"If every duck finishes at a shelter, everyone gains +{Reward(economy.AllTuckedInReward, economy)}; different shelters are fine, and worn-out ducks still qualify and add it before halving Stars. One unsheltered duck means no reward."
                    : $"If every duck finishes safely at a shelter, everyone gains +{Reward(economy.AllTuckedInReward, economy)}; different shelters are fine. One unsafe or unsheltered duck means no reward.",
                DuckWorldEventType.HomeBeforeDark => $"If every duck finishes safely, everyone gains +{Reward(economy.HomeBeforeDarkReward, economy)}. Shelters are not required, but one worn-out duck means no reward.",
                DuckWorldEventType.SharedSupper => $"If every duck physically places at least 1 Seed today, everyone gains +{Reward(economy.SharedSupperReward, economy)}. Worn-out ducks still qualify and add it before their {economy.CurrencyName} are halved.",
                DuckWorldEventType.StillAir => "The breeze is sleepy: Tailwind 2 moves 1, Tailwind 4 moves 2, and Tailwind 6 moves 3 today; their other powers are unchanged. A pending Log is used but never halves that Tailwind twice; if the Log's next Wish is not a Tailwind, it works normally.",
                DuckWorldEventType.ThickMorningMist => "The path is misty: Signposts still move 2 spaces total but preview no chip today, and no preview is saved for later.",
                DuckWorldEventType.RestlessNight => wornOutKeepsNightRewards
                    ? $"A duck that finishes at a shelter receives 1 less shelter-related {SingularCurrency(economy)}, to a minimum of 0 (printed shelter reward, Flowers, and Final Night shelter bonus together). Apply it to worn-out ducks before halving Stars. Twigs, Feathers, flock rewards, and other event rewards stay untouched."
                    : $"A duck that finishes safely at a shelter receives 1 less shelter-related {SingularCurrency(economy)}, to a minimum of 0 (printed shelter reward, Flowers, and Final Night shelter bonus together). Twigs, Feathers, flock rewards, and other event rewards stay untouched.",
                DuckWorldEventType.GloriousSunshine => $"Before exploring, every duck publicly chooses Fresh Air (+{freshAirExhaustionBonus} to today's safe Exhaustion maximum: {5 + freshAirExhaustionBonus} is safe, or {4 + freshAirExhaustionBonus} after an unprotected Goose) or Warm Dreams (+{Reward(economy.WarmDreamsReward, economy)} before Pebbles, wear-out halving, Most Rested, and Final Night conversion). Everyone chooses before anyone draws.",
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }

        public static string Event(DuckWorldEventDefinition worldEvent, DuckEconomyDefinition economy)
        {
            if (worldEvent == null) throw new ArgumentNullException(nameof(worldEvent));
            return Event(worldEvent.EventType, economy);
        }

        public static string Event(DuckWorldEventDefinition worldEvent, DuckRuleDefinitions rules)
        {
            if (worldEvent == null) throw new ArgumentNullException(nameof(worldEvent));
            return Event(worldEvent.EventType, rules);
        }

        public static string Offer(DuckShopOffer offer, DuckEconomyDefinition economy)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            if (economy == null) throw new ArgumentNullException(nameof(economy));

            return $"Take this Wish home for {Reward(offer.Price, economy)}. {Encounter(offer.Encounter, economy)}";
        }

        public static string Offer(DuckShopOffer offer, DuckRuleDefinitions rules)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return $"Take this Wish home for {Reward(offer.Price, rules.Economy)}. {Encounter(offer.Encounter, rules)}";
        }

        private static string Reward(int amount, DuckEconomyDefinition economy) =>
            amount == 0 && economy.UsesStars
                ? "no Stars"
                : $"{amount} {(amount == 1 ? SingularCurrency(economy) : economy.CurrencyName)}";

        private static string SingularCurrency(DuckEconomyDefinition economy) =>
            economy.UsesStars ? "Star" : economy.CurrencyName;
    }
}
