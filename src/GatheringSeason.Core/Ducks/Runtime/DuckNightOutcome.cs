using System;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Frozen display and history breakdown from one ordered Night resolution.</summary>
    public sealed class DuckNightOutcome
    {
        internal DuckNightOutcome(
            int day,
            int printedSleep,
            int printedTwigs,
            int reedsTwigs,
            int eventTwigs,
            int bramblesPenalty,
            int flowerSleep,
            int finalShelterSleep,
            int restlessNightPenalty,
            int collectiveEventSleep,
            int gloriousSunshineSleep,
            int flockSleep,
            int pebblesPenalty,
            int sleepBeforeWear,
            int frozenSleep,
            int totalTwigsEarned,
            int feathersAwarded,
            bool isMostRested,
            int nextDayTemporaryStep,
            int dreamTwigs)
        {
            ValidateNonNegative(printedSleep, nameof(printedSleep));
            ValidateNonNegative(printedTwigs, nameof(printedTwigs));
            ValidateNonNegative(reedsTwigs, nameof(reedsTwigs));
            ValidateNonNegative(eventTwigs, nameof(eventTwigs));
            ValidateNonNegative(bramblesPenalty, nameof(bramblesPenalty));
            ValidateNonNegative(flowerSleep, nameof(flowerSleep));
            ValidateNonNegative(finalShelterSleep, nameof(finalShelterSleep));
            ValidateNonNegative(restlessNightPenalty, nameof(restlessNightPenalty));
            ValidateNonNegative(collectiveEventSleep, nameof(collectiveEventSleep));
            ValidateNonNegative(gloriousSunshineSleep, nameof(gloriousSunshineSleep));
            ValidateNonNegative(flockSleep, nameof(flockSleep));
            ValidateNonNegative(pebblesPenalty, nameof(pebblesPenalty));
            ValidateNonNegative(sleepBeforeWear, nameof(sleepBeforeWear));
            ValidateNonNegative(frozenSleep, nameof(frozenSleep));
            ValidateNonNegative(totalTwigsEarned, nameof(totalTwigsEarned));
            ValidateNonNegative(feathersAwarded, nameof(feathersAwarded));
            ValidateNonNegative(dreamTwigs, nameof(dreamTwigs));
            if (day < 1 || day > DuckMatchSettings.StandardDays) throw new ArgumentOutOfRangeException(nameof(day));
            if (nextDayTemporaryStep < 0 || nextDayTemporaryStep > 1)
                throw new ArgumentOutOfRangeException(nameof(nextDayTemporaryStep));

            Day = day;
            PrintedSleep = printedSleep;
            PrintedTwigs = printedTwigs;
            ReedsTwigs = reedsTwigs;
            EventTwigs = eventTwigs;
            BramblesPenalty = bramblesPenalty;
            FlowerSleep = flowerSleep;
            FinalShelterSleep = finalShelterSleep;
            RestlessNightPenalty = restlessNightPenalty;
            CollectiveEventSleep = collectiveEventSleep;
            GloriousSunshineSleep = gloriousSunshineSleep;
            FlockSleep = flockSleep;
            PebblesPenalty = pebblesPenalty;
            SleepBeforeWear = sleepBeforeWear;
            FrozenSleep = frozenSleep;
            TotalTwigsEarned = totalTwigsEarned;
            FeathersAwarded = feathersAwarded;
            IsMostRested = isMostRested;
            NextDayTemporaryStep = nextDayTemporaryStep;
            DreamTwigs = dreamTwigs;
        }

        public int Day { get; }
        public int PrintedSleep { get; }
        public int PrintedReward => PrintedSleep;
        public int PrintedTwigs { get; }
        public int ReedsTwigs { get; }
        public int EventTwigs { get; }
        public int BramblesPenalty { get; }
        public int FlowerSleep { get; }
        public int FlowerReward => FlowerSleep;
        public int FinalShelterSleep { get; }
        public int FinalShelterReward => FinalShelterSleep;
        public int RestlessNightPenalty { get; }
        public int CollectiveEventSleep { get; }
        public int CollectiveEventReward => CollectiveEventSleep;
        public int GloriousSunshineSleep { get; }
        public int GloriousSunshineReward => GloriousSunshineSleep;
        public int FlockSleep { get; }
        public int FlockReward => FlockSleep;
        public int PebblesPenalty { get; }
        public int SleepBeforeWear { get; }
        public int RewardBeforeWear => SleepBeforeWear;
        public int FrozenSleep { get; }
        public int FrozenReward => FrozenSleep;
        public int FrozenStars => FrozenSleep;
        public int TotalTwigsEarned { get; }
        public int FeathersAwarded { get; }
        public bool IsMostRested { get; }
        public int NextDayTemporaryStep { get; }
        public int DreamTwigs { get; }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
