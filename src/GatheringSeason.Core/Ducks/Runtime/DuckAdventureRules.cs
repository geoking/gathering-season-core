using System;
using System.Collections.Generic;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>
    /// Pure authoritative encounter placement rules. The runtime applies this result
    /// to owned match state; policies may project public observations through the same
    /// transition without gaining bag order, random state or other private data.
    /// </summary>
    internal static class DuckAdventureRules
    {
        internal static DuckAdventurePlacement ApplyEncounter(
            DuckAdventureState state,
            DuckEncounterDefinition definition,
            DuckWorldEventType worldEvent)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var type = definition.EncounterType;
            var splashProtectionArmed = false;
            var guideProtectionAvailable = state.GuideProtectionAvailable;
            var splashSuppression = state.SplashProtectionArmed && definition.IsObstacle;
            var guideSuppression = definition.IsObstacle && guideProtectionAvailable;
            if (guideSuppression) guideProtectionAvailable = false;
            var nuisanceSuppressed = splashSuppression || guideSuppression;

            var activeFlock = state.ActiveFlock;
            int movement;
            if (type == DuckEncounterType.Companion)
            {
                activeFlock++;
                movement = Math.Min(activeFlock + 1, 4);
            }
            else
            {
                movement = definition.BaseMovement!.Value;
            }

            if (worldEvent == DuckWorldEventType.RainSoftenedSeeds && type == DuckEncounterType.Seeds)
                movement++;

            var logSlowdownPending = state.LogSlowdownPending;
            var alreadyHalved = false;
            if (worldEvent == DuckWorldEventType.StillAir && type == DuckEncounterType.Tailwind)
            {
                movement = HalveMovement(movement);
                alreadyHalved = true;
            }
            if (definition.IsHelpful && logSlowdownPending)
            {
                if (!alreadyHalved) movement = HalveMovement(movement);
                logSlowdownPending = false;
            }
            movement = Math.Max(1, movement);

            var position = Math.Min(43, state.Position + movement);
            var exhaustion = state.Exhaustion;
            var safeExhaustionMaximum = state.SafeExhaustionMaximum;
            var flowersPlaced = state.FlowersPlaced;
            var helpfulTypeMask = state.HelpfulTypeMask;
            var pocketDriftwoodAwarded = state.PocketDriftwoodAwarded;
            var reedsTwigs = 0;
            var eventTwigs = 0;

            if (definition.IsHelpful)
            {
                helpfulTypeMask = AddHelpfulType(helpfulTypeMask, type);
                switch (type)
                {
                    case DuckEncounterType.Reeds:
                        reedsTwigs = definition.TwigYield;
                        break;
                    case DuckEncounterType.Splash:
                        splashProtectionArmed = true;
                        break;
                    case DuckEncounterType.Wildflowers:
                        flowersPlaced++;
                        break;
                }
            }
            else
            {
                exhaustion += definition.ExhaustionValue;
                if (!nuisanceSuppressed)
                {
                    switch (type)
                    {
                        case DuckEncounterType.FallenLog:
                            logSlowdownPending = true;
                            break;
                        case DuckEncounterType.MudPuddle:
                            activeFlock = Math.Max(0, activeFlock - 1);
                            break;
                        case DuckEncounterType.GrumpyGoose:
                            safeExhaustionMaximum = Math.Max(0, safeExhaustionMaximum - 1);
                            break;
                    }
                }
            }

            if (worldEvent == DuckWorldEventType.PocketOfDriftwood
                && !pocketDriftwoodAwarded
                && HelpfulTypeCount(helpfulTypeMask) >= 3)
            {
                pocketDriftwoodAwarded = true;
                eventTwigs = 1;
            }

            var next = new DuckAdventureState(
                position,
                exhaustion,
                safeExhaustionMaximum,
                activeFlock,
                splashProtectionArmed,
                logSlowdownPending,
                guideProtectionAvailable,
                pocketDriftwoodAwarded,
                flowersPlaced,
                helpfulTypeMask);
            return new DuckAdventurePlacement(
                next,
                type,
                movement,
                nuisanceSuppressed,
                reedsTwigs,
                eventTwigs,
                exhaustion > safeExhaustionMaximum);
        }

        internal static int HelpfulTypes(IEnumerable<DuckEncounterType> types)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            var mask = 0;
            foreach (var type in types) mask = AddHelpfulType(mask, type);
            return mask;
        }

        internal static bool ContainsHelpfulType(int mask, DuckEncounterType type) =>
            (mask & TypeBit(type)) != 0;

        private static int AddHelpfulType(int mask, DuckEncounterType type) => mask | TypeBit(type);

        private static int HelpfulTypeCount(int mask)
        {
            var count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }
            return count;
        }

        private static int TypeBit(DuckEncounterType type)
        {
            if (!Enum.IsDefined(typeof(DuckEncounterType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            return 1 << (int)type;
        }

        private static int HalveMovement(int movement) => Math.Max(1, (movement + 1) / 2);
    }

    internal readonly struct DuckAdventureState
    {
        internal DuckAdventureState(
            int position,
            int exhaustion,
            int safeExhaustionMaximum,
            int activeFlock,
            bool splashProtectionArmed,
            bool logSlowdownPending,
            bool guideProtectionAvailable,
            bool pocketDriftwoodAwarded,
            int flowersPlaced,
            int helpfulTypeMask)
        {
            Position = position;
            Exhaustion = exhaustion;
            SafeExhaustionMaximum = safeExhaustionMaximum;
            ActiveFlock = activeFlock;
            SplashProtectionArmed = splashProtectionArmed;
            LogSlowdownPending = logSlowdownPending;
            GuideProtectionAvailable = guideProtectionAvailable;
            PocketDriftwoodAwarded = pocketDriftwoodAwarded;
            FlowersPlaced = flowersPlaced;
            HelpfulTypeMask = helpfulTypeMask;
        }

        internal int Position { get; }
        internal int Exhaustion { get; }
        internal int SafeExhaustionMaximum { get; }
        internal int ActiveFlock { get; }
        internal bool SplashProtectionArmed { get; }
        internal bool LogSlowdownPending { get; }
        internal bool GuideProtectionAvailable { get; }
        internal bool PocketDriftwoodAwarded { get; }
        internal int FlowersPlaced { get; }
        internal int HelpfulTypeMask { get; }
    }

    internal readonly struct DuckAdventurePlacement
    {
        internal DuckAdventurePlacement(
            DuckAdventureState state,
            DuckEncounterType encounterType,
            int movement,
            bool nuisanceSuppressed,
            int reedsTwigsAwarded,
            int eventTwigsAwarded,
            bool wearsOut)
        {
            State = state;
            EncounterType = encounterType;
            Movement = movement;
            NuisanceSuppressed = nuisanceSuppressed;
            ReedsTwigsAwarded = reedsTwigsAwarded;
            EventTwigsAwarded = eventTwigsAwarded;
            WearsOut = wearsOut;
        }

        internal DuckAdventureState State { get; }
        internal DuckEncounterType EncounterType { get; }
        internal int Movement { get; }
        internal bool NuisanceSuppressed { get; }
        internal int ReedsTwigsAwarded { get; }
        internal int EventTwigsAwarded { get; }
        internal bool WearsOut { get; }
    }
}
