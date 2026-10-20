using System.Collections.Generic;
using System.Linq;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Authoritative, Unity-independent Duck rules catalogue.</summary>
    public static class DuckRules
    {
        public const int CurrentRulesRevision = 8;

        /// <summary>The current catalogue for the Duck v1 product profile.</summary>
        public static DuckRuleDefinitions V1 { get; } = CreateV1();

        private static DuckRuleDefinitions CreateV1()
        {
            var encounters = new[]
            {
                Helpful("seeds", "Seeds", DuckEncounterType.Seeds, 1),
                Helpful("tailwind_2", "Tailwind 2", DuckEncounterType.Tailwind, 2),
                Helpful("tailwind_4", "Tailwind 4", DuckEncounterType.Tailwind, 4),
                Helpful("tailwind_6", "Tailwind 6", DuckEncounterType.Tailwind, 6),
                Helpful("signpost", "Signpost", DuckEncounterType.Signpost, 2),
                Helpful("splash", "Refreshing splash", DuckEncounterType.Splash, 1),
                Helpful("reeds_1", "Nesting reeds 1", DuckEncounterType.Reeds, 1, 1),
                Helpful("reeds_2", "Nesting reeds 2", DuckEncounterType.Reeds, 1, 2),
                Helpful("reeds_3", "Nesting reeds 3", DuckEncounterType.Reeds, 1, 3),
                new DuckEncounterDefinition("companion", "Companion duck", DuckEncounterType.Companion, null, 0, 0),
                Helpful("wildflowers", "Wildflowers", DuckEncounterType.Wildflowers, 1),
                Obstacle("fallen_log", "Fallen log", DuckEncounterType.FallenLog),
                Obstacle("mud_puddle", "Mud puddle", DuckEncounterType.MudPuddle),
                Obstacle("loose_pebbles", "Loose pebbles", DuckEncounterType.LoosePebbles),
                Obstacle("brambles", "Brambles", DuckEncounterType.Brambles),
                Obstacle("grumpy_goose", "Grumpy Goose", DuckEncounterType.GrumpyGoose)
            };

            var byId = encounters.ToDictionary(item => item.DefinitionId);
            var shopOffers = new[]
            {
                Offer("seeds", 0),
                Offer("tailwind_2", 1),
                Offer("tailwind_4", 2),
                Offer("tailwind_6", 3),
                Offer("signpost", 2),
                Offer("splash", 1),
                Offer("reeds_1", 2),
                Offer("reeds_2", 3),
                Offer("reeds_3", 4),
                Offer("companion", 2),
                Offer("wildflowers", 1)
            };

            var openingBagIds = new[]
            {
                "fallen_log", "fallen_log",
                "mud_puddle", "mud_puddle",
                "loose_pebbles", "loose_pebbles",
                "brambles", "brambles",
                "seeds", "seeds",
                "tailwind_2",
                "signpost",
                "splash"
            };

            return new DuckRuleDefinitions(
                CreateEconomy(),
                CreateBoard(),
                encounters,
                shopOffers,
                CreateWorldEvents(),
                openingBagIds.Select(id => byId[id]));

            DuckShopOffer Offer(string id, int price) => new DuckShopOffer(id, byId[id], price);
        }

        private static DuckEncounterDefinition Helpful(
            string id,
            string name,
            DuckEncounterType type,
            int movement,
            int twigYield = 0)
        {
            return new DuckEncounterDefinition(id, name, type, movement, twigYield, 0);
        }

        private static DuckEncounterDefinition Obstacle(string id, string name, DuckEncounterType type)
        {
            return new DuckEncounterDefinition(id, name, type, 1, 0, 1);
        }

        private static DuckEconomyDefinition CreateEconomy()
        {
            return new DuckEconomyDefinition(
                "Stars", usesStars: true,
                flowersPerChipReward: 1, flowersRewardLimit: 1,
                flockLeaderPerCompanionReward: 1, flockLeaderRewardLimit: 1,
                warmDreamsReward: 2,
                allTuckedInReward: 1,
                homeBeforeDarkReward: 1,
                sharedSupperReward: 1,
                finalShelterReward: 1,
                finalRewardPerDreamTwig: 1);
        }

        private static IEnumerable<DuckBoardSpace> CreateBoard()
        {
            return new[]
            {
                Space(1, DuckBiome.Wetlands, 1, 1),
                Space(2, DuckBiome.Wetlands, 1, 1),
                Space(3, DuckBiome.Wetlands, 1, 1),
                Shelter(4, DuckBiome.Wetlands, 2, 1, 1, "Reed hammock"),
                Space(5, DuckBiome.Wetlands, 1, 2),
                Space(6, DuckBiome.Wetlands, 1, 2),
                Space(7, DuckBiome.Wetlands, 1, 2),
                Space(8, DuckBiome.Wetlands, 1, 2),
                Space(9, DuckBiome.Wetlands, 1, 3),
                Shelter(10, DuckBiome.Wetlands, 2, 3, 1, "Willow nest"),
                Space(11, DuckBiome.Wetlands, 1, 3),
                Space(12, DuckBiome.Wetlands, 1, 3),
                Space(13, DuckBiome.Wetlands, 1, 3),
                Space(14, DuckBiome.Wetlands, 1, 3),
                Space(15, DuckBiome.Meadow, 2, 4),
                Space(16, DuckBiome.Meadow, 2, 4),
                Shelter(17, DuckBiome.Meadow, 3, 4, 1, "Clover hollow"),
                Space(18, DuckBiome.Meadow, 2, 4),
                Space(19, DuckBiome.Meadow, 2, 4),
                Space(20, DuckBiome.Meadow, 2, 5),
                Shelter(21, DuckBiome.Meadow, 3, 5, 1, "Orchard shelter"),
                Space(22, DuckBiome.Meadow, 2, 5),
                Space(23, DuckBiome.Meadow, 2, 5),
                Space(24, DuckBiome.Meadow, 2, 5),
                Shelter(25, DuckBiome.Meadow, 3, 5, 1, "Hayloft hideaway"),
                Space(26, DuckBiome.Meadow, 2, 5),
                Space(27, DuckBiome.Meadow, 2, 5),
                Space(28, DuckBiome.Meadow, 2, 5),
                Space(29, DuckBiome.Wasteland, 1, 6),
                Space(30, DuckBiome.Wasteland, 1, 7),
                Space(31, DuckBiome.Wasteland, 1, 7),
                Shelter(32, DuckBiome.Wasteland, 4, 7, 2, "Shaded rock nook"),
                Space(33, DuckBiome.Wasteland, 1, 7),
                Space(34, DuckBiome.Wasteland, 1, 8),
                Space(35, DuckBiome.Wasteland, 1, 8),
                Shelter(36, DuckBiome.Wasteland, 4, 8, 2, "Spring-fed refuge"),
                Space(37, DuckBiome.Wasteland, 1, 8),
                Space(38, DuckBiome.Wasteland, 1, 8),
                Space(39, DuckBiome.Wasteland, 1, 8),
                Space(40, DuckBiome.Wasteland, 1, 8),
                Space(41, DuckBiome.Wasteland, 1, 8),
                Space(42, DuckBiome.Wasteland, 1, 8),
                Shelter(43, DuckBiome.Wasteland, 5, 9, 2, "Oasis sanctuary")
            };
        }

        private static DuckBoardSpace Space(int space, DuckBiome biome, int sleep, int twigs)
        {
            return new DuckBoardSpace(space, biome, sleep, twigs, 0, null);
        }

        private static DuckBoardSpace Shelter(
            int space,
            DuckBiome biome,
            int sleep,
            int twigs,
            int feathers,
            string name)
        {
            return new DuckBoardSpace(space, biome, sleep, twigs, feathers, name);
        }

        private static IEnumerable<DuckWorldEventDefinition> CreateWorldEvents()
        {
            return new[]
            {
                Event("rain_softened_seeds", DuckWorldEventType.RainSoftenedSeeds, "Gentle Rain"),
                Event("glorious_sunshine", DuckWorldEventType.GloriousSunshine, "Glorious Sunshine"),
                Event("a_friendly_guide", DuckWorldEventType.FriendlyGuide, "Clearing Breeze"),
                Event("a_pocket_of_driftwood", DuckWorldEventType.PocketOfDriftwood, "Windfall Gusts"),
                Event("all_tucked_in", DuckWorldEventType.AllTuckedIn, "Evening Chill"),
                Event("home_before_dark", DuckWorldEventType.HomeBeforeDark, "Golden Sunset"),
                Event("shared_supper", DuckWorldEventType.SharedSupper, "Morning Dew"),
                Event("still_air", DuckWorldEventType.StillAir, "Still Air"),
                Event("thick_morning_mist", DuckWorldEventType.ThickMorningMist, "Thick Morning Mist"),
                Event("restless_night", DuckWorldEventType.RestlessNight, "Thundery Skies"),
                Event("golden_morning", DuckWorldEventType.GoldenMorning, "Golden Morning"),
                Event("refreshing_showers", DuckWorldEventType.RefreshingShowers, "Refreshing Showers"),
                Event("favourable_winds", DuckWorldEventType.FavourableWinds, "Favourable Winds"),
                Event("crosswinds", DuckWorldEventType.Crosswinds, "Crosswinds"),
                Event("low_cloud", DuckWorldEventType.LowCloud, "Low Cloud")
            };
        }

        private static DuckWorldEventDefinition Event(string id, DuckWorldEventType type, string name)
        {
            return new DuckWorldEventDefinition(id, type, name);
        }
    }
}
