using System;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Conservative reachable-state bounds from the retained catalogue and calendar.</summary>
    internal sealed class DuckStateLimits
    {
        internal DuckStateLimits(DuckRuleDefinitions rules, int day)
        {
            Inventory = rules.OpeningBag.Count + (day >= 5 ? 1 : 0);
            for (var night = 1; night <= day; night++)
                Inventory += DuckDreamHandler.PurchaseLimitForDay(night);
            Exhaustion = Inventory * rules.EncounterDefinitions.Max(chip => chip.ExhaustionValue);
            ReedsTwigs = Inventory * rules.EncounterDefinitions.Max(chip => chip.TwigYield);
            var economy = rules.Economy;
            FlowersReward = (int)Math.Min(economy.FlowersRewardLimit, (long)Inventory * economy.FlowersPerChipReward);
            Reward = rules.BoardSpaces.Max(space => space.Reward) + FlowersReward
                + economy.FinalShelterReward + economy.WarmDreamsReward
                + Math.Max(economy.AllTuckedInReward, Math.Max(economy.HomeBeforeDarkReward, economy.SharedSupperReward))
                + (int)Math.Min(economy.FlockLeaderRewardLimit, (long)Inventory * economy.FlockLeaderPerCompanionReward);
            DreamTwigs = economy.ConvertFinalRewardToDreamTwigs(Reward) + 1;
            DailyTwigs = rules.BoardSpaces.Max(space => space.Twigs) + ReedsTwigs + 1;
            TotalTwigs = day * DailyTwigs + (day == DuckMatchSettings.StandardDays ? DreamTwigs : 0);
            FeatherTrail = day * rules.BoardSpaces.Max(space => space.Feathers) + (day - 1) * 3;
            for (var currentDay = 1; currentDay <= day; currentDay++)
            {
                // Every chip can be drawn once; allow Rest, weather choice, purchases,
                // Dream completion and calendar advancement for each seat each Day.
                var drawLimit = rules.OpeningBag.Count + (currentDay >= 5 ? 1 : 0);
                for (var night = 1; night < currentDay; night++)
                    drawLimit += DuckDreamHandler.PurchaseLimitForDay(night);
                CommandRevision += drawLimit + DuckDreamHandler.PurchaseLimitForDay(currentDay) + 4;
            }
        }

        internal int Inventory { get; }
        internal int Exhaustion { get; }
        internal int ReedsTwigs { get; }
        internal int FlowersReward { get; }
        internal int Reward { get; }
        internal int DreamTwigs { get; }
        internal int DailyTwigs { get; }
        internal int TotalTwigs { get; }
        internal int FeatherTrail { get; }
        internal long CommandRevision { get; }

        /// <summary>Check arithmetic headroom for the complete possible cohort before any mutation.</summary>
        internal static void GuardMutation(DuckMatchState state, DuckRuleDefinitions rules, GameAction action, DuckPlayerState actor)
        {
            var capacityDay = action.Kind == GameActionKind.NextDay ? state.Day + 1 : state.Day;
            var capacity = new DuckStateLimits(rules, capacityDay);
            foreach (var player in state.Players)
            {
                Headroom(player.Exhaustion, rules.EncounterDefinitions.Max(chip => chip.ExhaustionValue));
                Headroom(player.ActiveFlock, 2);
                Headroom(player.FlowersPlaced, 1);
                Headroom(player.SafeExhaustionMaximum, rules.FreshAirExhaustionBonus);
                Headroom(player.Position, 7);
                var maximumTwigs = (long)player.DayReedsTwigs + player.DayEventTwigs
                    + rules.EncounterDefinitions.Max(chip => chip.TwigYield) + 1
                    + rules.BoardSpaces.Max(space => space.Twigs);
                var economy = rules.Economy;
                var maximumReward = rules.BoardSpaces.Max(space => space.Reward)
                    + Math.Min(economy.FlowersRewardLimit, ((long)player.FlowersPlaced + 1) * economy.FlowersPerChipReward)
                    + Math.Min(economy.FlockLeaderRewardLimit, ((long)player.ActiveFlock + 1) * economy.FlockLeaderPerCompanionReward)
                    + economy.FinalShelterReward + economy.WarmDreamsReward
                    + Math.Max(economy.AllTuckedInReward, Math.Max(economy.HomeBeforeDarkReward, economy.SharedSupperReward));
                Headroom(player.DayReedsTwigs, maximumTwigs - player.DayReedsTwigs);
                Headroom(player.DayEventTwigs, maximumTwigs - player.DayEventTwigs);
                Headroom(0, maximumReward);
                Headroom(player.TotalTwigs, maximumTwigs + maximumReward / economy.FinalRewardPerDreamTwig + 1);
                Headroom(player.PermanentFeatherTrail, rules.BoardSpaces.Max(space => space.Feathers) + 4);
                Headroom(player.EffectiveStart, 7);
                if (state.Phase == DuckPhase.Adventure)
                {
                    // Project each possible Explore before a final-Day cohort can reveal.
                    // This also verifies bounded counters without consuming any pouch chip.
                    var reeds = 0;
                    var eventTwigs = 0;
                    if (!player.HasFinishedDay && player.BagPhysicalChipIds.Count > 0)
                    {
                        var chip = player.Inventory.Single(item => item.PhysicalChipId == player.BagPhysicalChipIds[0]);
                        var before = new DuckAdventureState(player.Position, player.Exhaustion,
                            player.SafeExhaustionMaximum, player.ActiveFlock, player.SplashProtectionArmed,
                            player.LogSlowdownPending, player.GuideProtectionAvailable, player.PocketDriftwoodAwarded,
                            player.FlowersPlaced, DuckAdventureRules.HelpfulTypes(player.PlacedHelpfulTypes));
                        var placement = DuckAdventureRules.ApplyEncounter(before, rules.Encounter(chip.DefinitionId),
                            rules.WorldEvent(state.WorldEventDeckDefinitionIds[state.CurrentEventIndex]).EventType, rules);
                        Capacity(placement.State.Exhaustion, capacity.Exhaustion);
                        Capacity(placement.State.ActiveFlock, capacity.Inventory);
                        Capacity(placement.State.FlowersPlaced, capacity.Inventory);
                        reeds = placement.ReedsTwigsAwarded;
                        eventTwigs = placement.EventTwigsAwarded;
                    }
                    Capacity((long)player.DayReedsTwigs + reeds, capacity.ReedsTwigs);
                    Capacity((long)player.DayEventTwigs + eventTwigs, 1);
                    Capacity((long)player.TotalTwigs + reeds + eventTwigs
                        + rules.BoardSpaces.Max(space => space.Twigs)
                        + (state.Day == DuckMatchSettings.StandardDays ? capacity.DreamTwigs : 0), capacity.TotalTwigs);
                    Capacity((long)player.PermanentFeatherTrail + rules.BoardSpaces.Max(space => space.Feathers),
                        capacity.FeatherTrail);
                }
                if (action.Kind == GameActionKind.NextDay)
                {
                    Capacity((long)player.PermanentFeatherTrail + 3, capacity.FeatherTrail);
                    Capacity((long)player.PermanentFeatherTrail + 4, capacity.FeatherTrail + 1);
                    var deficit = state.Players.Max(candidate => candidate.TotalTwigs) - player.TotalTwigs;
                    var nextStart = (long)player.PermanentFeatherTrail
                        + DuckDayPreparation.DawnFeathersForDeficit(deficit)
                        + (player.PendingMostRestedStep ? 1 : 0);
                    Capacity(nextStart, rules.BoardSpaces.Count);
                }
            }
            if (action.Kind == GameActionKind.BuyEncounter)
            {
                Capacity((long)state.NextPhysicalChipId + 1, capacity.Inventory * state.Players.Count + 1);
                Capacity((long)actor.Inventory.Count + 1, capacity.Inventory);
            }
            if (action.Kind == GameActionKind.NextDay && state.Day == 4 && !state.DayFiveGooseAdded)
                Capacity((long)state.NextPhysicalChipId + state.Players.Count,
                    capacity.Inventory * state.Players.Count + 1);
            if (state.Day == DuckMatchSettings.StandardDays && state.Phase == DuckPhase.Adventure)
                Capacity((long)state.FinalDayDecisionBeat + 1, capacity.Inventory + 1);
            if (action.Kind == GameActionKind.BuyEncounter)
                Headroom(state.NextPhysicalChipId, 1);
            if (action.Kind == GameActionKind.NextDay && state.Day == 4 && !state.DayFiveGooseAdded)
                Headroom(state.NextPhysicalChipId, state.Players.Count);
            if (state.Day == DuckMatchSettings.StandardDays && state.Phase == DuckPhase.Adventure)
                Headroom(state.FinalDayDecisionBeat, 1);
        }

        private static void Capacity(long value, long maximum)
        {
            if (value < 0 || value > maximum)
                throw new InvalidOperationException("The match state has exhausted its catalogue/calendar capacity.");
        }

        private static void Headroom(int value, long addition)
        {
            if (value < 0 || (long)value + addition > int.MaxValue)
                throw new InvalidOperationException("The match state has exhausted an arithmetic counter.");
        }
    }
}
