using System;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Immutable reward, bonus and final-conversion values for one rules revision.</summary>
    public sealed class DuckEconomyDefinition
    {
        internal DuckEconomyDefinition(
            string currencyName,
            bool usesStars,
            int flowersPerChipReward,
            int flowersRewardLimit,
            int flockLeaderPerCompanionReward,
            int flockLeaderRewardLimit,
            int warmDreamsReward,
            int allTuckedInReward,
            int homeBeforeDarkReward,
            int sharedSupperReward,
            int finalShelterReward,
            int finalRewardPerDreamTwig,
            int pebblesPenalty = 1,
            int restlessNightPenalty = 1,
            int wearOutDivisor = 2)
        {
            if (string.IsNullOrWhiteSpace(currencyName)) throw new ArgumentException("A currency name is required.", nameof(currencyName));
            if (flowersPerChipReward < 0) throw new ArgumentOutOfRangeException(nameof(flowersPerChipReward));
            if (flowersRewardLimit < 0) throw new ArgumentOutOfRangeException(nameof(flowersRewardLimit));
            if (flockLeaderPerCompanionReward < 0) throw new ArgumentOutOfRangeException(nameof(flockLeaderPerCompanionReward));
            if (flockLeaderRewardLimit < 0) throw new ArgumentOutOfRangeException(nameof(flockLeaderRewardLimit));
            if (warmDreamsReward < 0) throw new ArgumentOutOfRangeException(nameof(warmDreamsReward));
            if (allTuckedInReward < 0) throw new ArgumentOutOfRangeException(nameof(allTuckedInReward));
            if (homeBeforeDarkReward < 0) throw new ArgumentOutOfRangeException(nameof(homeBeforeDarkReward));
            if (sharedSupperReward < 0) throw new ArgumentOutOfRangeException(nameof(sharedSupperReward));
            if (finalShelterReward < 0) throw new ArgumentOutOfRangeException(nameof(finalShelterReward));
            if (finalRewardPerDreamTwig <= 0) throw new ArgumentOutOfRangeException(nameof(finalRewardPerDreamTwig));
            if (pebblesPenalty < 0) throw new ArgumentOutOfRangeException(nameof(pebblesPenalty));
            if (restlessNightPenalty < 0) throw new ArgumentOutOfRangeException(nameof(restlessNightPenalty));
            if (wearOutDivisor <= 0) throw new ArgumentOutOfRangeException(nameof(wearOutDivisor));

            CurrencyName = currencyName;
            UsesStars = usesStars;
            FlowersPerChipReward = flowersPerChipReward;
            FlowersRewardLimit = flowersRewardLimit;
            FlockLeaderPerCompanionReward = flockLeaderPerCompanionReward;
            FlockLeaderRewardLimit = flockLeaderRewardLimit;
            WarmDreamsReward = warmDreamsReward;
            AllTuckedInReward = allTuckedInReward;
            HomeBeforeDarkReward = homeBeforeDarkReward;
            SharedSupperReward = sharedSupperReward;
            FinalShelterReward = finalShelterReward;
            FinalRewardPerDreamTwig = finalRewardPerDreamTwig;
            PebblesPenalty = pebblesPenalty;
            RestlessNightPenalty = restlessNightPenalty;
            WearOutDivisor = wearOutDivisor;
        }

        public string CurrencyName { get; }
        public bool UsesStars { get; }
        public int FlowersPerChipReward { get; }
        public int FlowersRewardLimit { get; }
        public int FlockLeaderPerCompanionReward { get; }
        public int FlockLeaderRewardLimit { get; }
        public int WarmDreamsReward { get; }
        public int AllTuckedInReward { get; }
        public int HomeBeforeDarkReward { get; }
        public int SharedSupperReward { get; }
        public int FinalShelterReward { get; }
        public int FinalRewardPerDreamTwig { get; }
        public int PebblesPenalty { get; }
        public int RestlessNightPenalty { get; }
        public int WearOutDivisor { get; }

        public int CalculateFlowerReward(int flowersPlaced)
        {
            if (flowersPlaced < 0) throw new ArgumentOutOfRangeException(nameof(flowersPlaced));
            return Math.Min(FlowersRewardLimit, flowersPlaced * FlowersPerChipReward);
        }

        public int CalculateFlockLeaderReward(int activeFlock)
        {
            if (activeFlock < 0) throw new ArgumentOutOfRangeException(nameof(activeFlock));
            return Math.Min(FlockLeaderRewardLimit, activeFlock * FlockLeaderPerCompanionReward);
        }

        public int ConvertFinalRewardToDreamTwigs(int retainedReward)
        {
            if (retainedReward < 0) throw new ArgumentOutOfRangeException(nameof(retainedReward));
            return retainedReward / FinalRewardPerDreamTwig;
        }
    }
}
