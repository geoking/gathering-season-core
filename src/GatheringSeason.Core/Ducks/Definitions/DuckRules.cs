using System.Collections.Generic;
using System.Linq;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Authoritative, Unity-independent Duck rules catalogues.</summary>
    public static class DuckRules
    {
        public const int CurrentRulesRevision = 7;

        private static readonly DuckRuleDefinitions Revision1 = CreateV1(rulesRevision: 1);
        private static readonly DuckRuleDefinitions Revision2 = CreateV1(rulesRevision: 2);
        private static readonly DuckRuleDefinitions Revision3 = CreateV1(rulesRevision: 3);
        private static readonly DuckRuleDefinitions Revision4 = CreateV1(rulesRevision: 4);
        private static readonly DuckRuleDefinitions Revision5 = CreateV1(rulesRevision: 5);
        private static readonly DuckRuleDefinitions Revision6 = CreateV1(rulesRevision: 6);

        /// <summary>The current catalogue for the Duck v1 product profile.</summary>
        public static DuckRuleDefinitions V1 { get; } = CreateV1(CurrentRulesRevision);

        public static DuckRuleDefinitions ForRulesRevision(int rulesRevision)
        {
            return rulesRevision switch
            {
                1 => Revision1,
                2 => Revision2,
                3 => Revision3,
                4 => Revision4,
                5 => Revision5,
                6 => Revision6,
                CurrentRulesRevision => V1,
                _ => throw new System.ArgumentOutOfRangeException(nameof(rulesRevision),
                    "Unsupported Duck rules revision.")
            };
        }

        private static DuckRuleDefinitions CreateV1(int rulesRevision)
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
            var useRevisionTwoPrices = rulesRevision >= 2;
            var usesStars = rulesRevision >= 4;
            var shopOffers = new[]
            {
                Offer("seeds", usesStars ? 0 : 3),
                Offer("tailwind_2", usesStars ? 1 : useRevisionTwoPrices ? 4 : 5),
                Offer("tailwind_4", usesStars ? 2 : useRevisionTwoPrices ? 8 : 10),
                Offer("tailwind_6", usesStars ? 3 : useRevisionTwoPrices ? 12 : 15),
                Offer("signpost", usesStars ? 2 : 7),
                Offer("splash", usesStars ? 1 : 4),
                Offer("reeds_1", usesStars ? 2 : useRevisionTwoPrices ? 8 : 6),
                Offer("reeds_2", usesStars ? 3 : useRevisionTwoPrices ? 14 : 11),
                Offer("reeds_3", usesStars ? 4 : useRevisionTwoPrices ? 20 : 16),
                Offer("companion", usesStars ? 2 : 7),
                Offer("wildflowers", usesStars ? 1 : 5)
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
                rulesRevision,
                rulesRevision >= 6 ? 2 : 3,
                CreateEconomy(rulesRevision),
                CreateBoard(rulesRevision),
                encounters,
                shopOffers,
                CreateWorldEvents(rulesRevision),
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

        private static DuckEconomyDefinition CreateEconomy(int rulesRevision)
        {
            return rulesRevision >= 4
                ? new DuckEconomyDefinition(
                    "Stars", usesStars: true,
                    flowersPerChipReward: 1, flowersRewardLimit: 1,
                    flockLeaderPerCompanionReward: 1, flockLeaderRewardLimit: 1,
                    warmDreamsReward: 2,
                    allTuckedInReward: 1,
                    homeBeforeDarkReward: 1,
                    sharedSupperReward: 1,
                    finalShelterReward: 1,
                    finalRewardPerDreamTwig: 1)
                : new DuckEconomyDefinition(
                    "Sleep", usesStars: false,
                    flowersPerChipReward: 2, flowersRewardLimit: int.MaxValue,
                    flockLeaderPerCompanionReward: 1, flockLeaderRewardLimit: 2,
                    warmDreamsReward: 8,
                    allTuckedInReward: 2,
                    homeBeforeDarkReward: 1,
                    sharedSupperReward: 1,
                    finalShelterReward: 2,
                    finalRewardPerDreamTwig: 4);
        }

        private static IEnumerable<DuckBoardSpace> CreateBoard(int rulesRevision)
        {
            var legacy = CreateLegacyBoard().ToArray();
            if (rulesRevision < 4) return legacy;
            if (rulesRevision >= 7) return legacy.Select(RevisionSixSpace).Select(RevisionSevenSpace);
            if (rulesRevision == 6) return legacy.Select(RevisionSixSpace);
            return legacy.Select(space => new DuckBoardSpace(
                space.Space,
                space.Biome,
                RevisionFourReward(space),
                space.Twigs,
                space.Feathers,
                space.ShelterName));
        }

        private static DuckBoardSpace RevisionSixSpace(DuckBoardSpace space)
        {
            return space.Space switch
            {
                21 => new DuckBoardSpace(21, DuckBiome.Meadow, 2, 5, 0, null),
                22 => new DuckBoardSpace(22, DuckBiome.Meadow, 3, 5, 1, "Orchard shelter"),
                25 => new DuckBoardSpace(25, DuckBiome.Meadow, 3, 5, 1, "Hayloft hideaway"),
                26 => new DuckBoardSpace(26, DuckBiome.Meadow, 2, 5, 0, null),
                28 => new DuckBoardSpace(28, DuckBiome.Meadow, 2, 5, 0, null),
                _ => new DuckBoardSpace(
                    space.Space,
                    space.Biome,
                    RevisionFourReward(space),
                    space.Twigs,
                    space.Feathers,
                    space.ShelterName)
            };
        }

        private static DuckBoardSpace RevisionSevenSpace(DuckBoardSpace space)
        {
            return space.Space switch
            {
                16 => new DuckBoardSpace(16, DuckBiome.Meadow, 2, 4, 0, null),
                17 => new DuckBoardSpace(17, DuckBiome.Meadow, 3, 4, 1, "Clover hollow"),
                21 => new DuckBoardSpace(21, DuckBiome.Meadow, 3, 5, 1, "Orchard shelter"),
                22 => new DuckBoardSpace(22, DuckBiome.Meadow, 2, 5, 0, null),
                _ => new DuckBoardSpace(
                    space.Space,
                    space.Biome,
                    space.Reward,
                    space.Twigs,
                    space.Feathers,
                    space.ShelterName)
            };
        }

        private static int RevisionFourReward(DuckBoardSpace space)
        {
            if (space.Space == 43) return 5;
            if (space.IsShelter) return space.Space <= 14 ? 2 : space.Space <= 28 ? 3 : 4;
            return space.Space <= 14 ? 1 : space.Space <= 28 ? 2 : 1;
        }

        private static IEnumerable<DuckBoardSpace> CreateLegacyBoard()
        {
            return new[]
            {
                Space(1, DuckBiome.Wetlands, 3, 1),
                Space(2, DuckBiome.Wetlands, 3, 1),
                Space(3, DuckBiome.Wetlands, 5, 1),
                Shelter(4, DuckBiome.Wetlands, 6, 1, 1, "Reed hammock"),
                Space(5, DuckBiome.Wetlands, 5, 2),
                Space(6, DuckBiome.Wetlands, 6, 2),
                Space(7, DuckBiome.Wetlands, 6, 2),
                Space(8, DuckBiome.Wetlands, 6, 2),
                Space(9, DuckBiome.Wetlands, 7, 3),
                Shelter(10, DuckBiome.Wetlands, 10, 3, 1, "Willow nest"),
                Space(11, DuckBiome.Wetlands, 8, 3),
                Space(12, DuckBiome.Wetlands, 8, 3),
                Space(13, DuckBiome.Wetlands, 9, 3),
                Space(14, DuckBiome.Wetlands, 9, 3),
                Space(15, DuckBiome.Meadow, 10, 4),
                Shelter(16, DuckBiome.Meadow, 13, 4, 1, "Clover hollow"),
                Space(17, DuckBiome.Meadow, 11, 4),
                Space(18, DuckBiome.Meadow, 11, 4),
                Space(19, DuckBiome.Meadow, 11, 4),
                Space(20, DuckBiome.Meadow, 12, 5),
                Shelter(21, DuckBiome.Meadow, 15, 5, 1, "Orchard shelter"),
                Space(22, DuckBiome.Meadow, 13, 5),
                Space(23, DuckBiome.Meadow, 13, 5),
                Space(24, DuckBiome.Meadow, 13, 5),
                Space(25, DuckBiome.Meadow, 14, 5),
                Shelter(26, DuckBiome.Meadow, 16, 5, 1, "Hayloft hideaway"),
                Space(27, DuckBiome.Meadow, 14, 5),
                Space(28, DuckBiome.Meadow, 14, 6),
                Space(29, DuckBiome.Wasteland, 11, 6),
                Space(30, DuckBiome.Wasteland, 11, 7),
                Space(31, DuckBiome.Wasteland, 11, 7),
                Shelter(32, DuckBiome.Wasteland, 18, 7, 2, "Shaded rock nook"),
                Space(33, DuckBiome.Wasteland, 10, 7),
                Space(34, DuckBiome.Wasteland, 11, 8),
                Space(35, DuckBiome.Wasteland, 12, 8),
                Shelter(36, DuckBiome.Wasteland, 20, 8, 2, "Spring-fed refuge"),
                Space(37, DuckBiome.Wasteland, 12, 8),
                Space(38, DuckBiome.Wasteland, 12, 8),
                Space(39, DuckBiome.Wasteland, 11, 8),
                Space(40, DuckBiome.Wasteland, 12, 8),
                Space(41, DuckBiome.Wasteland, 13, 8),
                Space(42, DuckBiome.Wasteland, 13, 8),
                Shelter(43, DuckBiome.Wasteland, 21, 9, 2, "Oasis sanctuary")
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

        private static IEnumerable<DuckWorldEventDefinition> CreateWorldEvents(int rulesRevision)
        {
            return new[]
            {
                Event("rain_softened_seeds", DuckWorldEventType.RainSoftenedSeeds, "Rain-Softened Seeds"),
                rulesRevision >= 3
                    ? Event("glorious_sunshine", DuckWorldEventType.GloriousSunshine, "Glorious Sunshine")
                    : Event("sunlit_signboards", DuckWorldEventType.SunlitSignboards, "Sunlit Signboards"),
                Event("a_friendly_guide", DuckWorldEventType.FriendlyGuide, "A Friendly Guide"),
                Event("a_pocket_of_driftwood", DuckWorldEventType.PocketOfDriftwood, "A Pocket of Driftwood"),
                Event("all_tucked_in", DuckWorldEventType.AllTuckedIn, "All Tucked In"),
                Event("home_before_dark", DuckWorldEventType.HomeBeforeDark, "Home Before Dark"),
                Event("shared_supper", DuckWorldEventType.SharedSupper, "Shared Supper"),
                Event("still_air", DuckWorldEventType.StillAir, "Still Air"),
                Event("thick_morning_mist", DuckWorldEventType.ThickMorningMist, "Thick Morning Mist"),
                Event("restless_night", DuckWorldEventType.RestlessNight, "Restless Night")
            };
        }

        private static DuckWorldEventDefinition Event(string id, DuckWorldEventType type, string name)
        {
            return new DuckWorldEventDefinition(id, type, name);
        }
    }
}
