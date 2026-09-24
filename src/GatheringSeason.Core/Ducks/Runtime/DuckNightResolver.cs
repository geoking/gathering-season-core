using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Collective, ordered Night scoring. No client computes or awards these values.</summary>
    internal static class DuckNightResolver
    {
        internal static IReadOnlyDictionary<string, DuckNightOutcome> Calculate(DuckMatchState state, DuckRuleDefinitions rules)
        {
            if (state.Players.Count == 0 || state.Players.Any(player => !player.HasFinishedDay || player.PlacedChips.Count == 0))
                throw new InvalidOperationException("Night scoring waits until every duck has drawn and finished.");
            if (state.Players.Any(player => player.IsSleepFrozen))
                throw new InvalidOperationException("This Night has already been resolved.");

            var worldEvent = rules.WorldEvent(state.WorldEventDeckDefinitionIds[state.CurrentEventIndex]).EventType;
            var allSafe = state.Players.All(player => !player.IsWornOut);
            var allShelters = state.Players.All(player => rules.BoardSpaceAt(player.Position).IsShelter);
            var allSeeds = state.Players.All(player => player.PlacedHelpfulTypes.Contains(DuckEncounterType.Seeds));
            var economy = rules.Economy;
            var allTuckedIn = allShelters && (rules.RulesRevision >= 5 || allSafe);
            var eventSleep = worldEvent == DuckWorldEventType.AllTuckedIn && allTuckedIn ? economy.AllTuckedInReward
                : worldEvent == DuckWorldEventType.HomeBeforeDark && allSafe ? economy.HomeBeforeDarkReward
                : worldEvent == DuckWorldEventType.SharedSupper && allSeeds ? economy.SharedSupperReward : 0;
            var competingFlocks = rules.RulesRevision >= 5
                ? state.Players
                : state.Players.Where(player => !player.IsWornOut);
            var greatestFlock = competingFlocks.Select(player => player.ActiveFlock).DefaultIfEmpty(0).Max();
            var amounts = state.Players.Select(player => new NightAmounts(
                state.Day, player, rules, worldEvent, eventSleep, greatestFlock)).ToArray();
            var bestSafeSleep = amounts.Where(amount => !amount.Player.IsWornOut).Select(amount => amount.FrozenSleep).DefaultIfEmpty(-1).Max();
            return amounts.ToDictionary(amount => amount.Player.Id, amount => amount.ToOutcome(
                !amount.Player.IsWornOut && amount.FrozenSleep == bestSafeSleep), StringComparer.Ordinal);
        }

        internal static void Resolve(DuckMatchState state, DuckRuleDefinitions rules)
        {
            // Calculate the complete cohort before awarding anything. Immediate Reeds/Pocket Twigs
            // are already in the nest; add only the printed amount less final Brambles here.
            var outcomes = Calculate(state, rules);
            foreach (var player in state.Players)
            {
                var outcome = outcomes[player.Id];
                player.TotalTwigs += outcome.PrintedTwigs - outcome.BramblesPenalty + outcome.DreamTwigs;
                player.FrozenSleep = outcome.FrozenSleep;
                player.RemainingSleep = state.Day == DuckMatchSettings.StandardDays ? 0 : outcome.FrozenSleep;
                player.IsSleepFrozen = true;
                player.PendingMostRestedStep = outcome.NextDayTemporaryStep == 1;
                player.LastNightOutcome = outcome;
                player.HasFinishedDream = state.Day == DuckMatchSettings.StandardDays;
                DuckFeatherAwards.Give(state, player, outcome.FeathersAwarded, "shelter");
                state.History.Add(new DuckHistoryState(state.Day, player.Id,
                    $"{player.Name} rests at {player.Position}: {Quantity(outcome.TotalTwigsEarned, "Twig")} added to the nest, {Reward(outcome.FrozenReward, rules.Economy)}" +
                    (player.IsWornOut ? " after worn-out halving." : ".")));
                if (outcome.IsMostRested)
                {
                    state.PublicAwards.Add(new DuckPublicAwardState(state.Day, "most_rested", new[] { player.Id }, 0, 0, 0));
                    state.History.Add(new DuckHistoryState(state.Day, player.Id, $"{player.Name} is a Most Rested Duck."));
                }
            }
            if (state.Day == DuckMatchSettings.StandardDays)
            {
                state.FinalResult = DuckFinalResult.Create(state.Players);
                state.Phase = DuckPhase.Finished;
                state.History.Add(new DuckHistoryState(state.Day, string.Empty,
                    "Final result: " + string.Join(", ", state.FinalResult.Standings.Select(standing =>
                        $"{standing.PlayerName} {Quantity(standing.TotalTwigs, "Nest Twig")} / {Reward(standing.FrozenNightTenReward, rules.Economy)}")) + "."));
            }
            else
            {
                state.Phase = DuckPhase.Night;
            }
        }

        private static string Reward(int amount, DuckEconomyDefinition economy) =>
            amount == 0 && economy.UsesStars
                ? "no Stars"
                : $"{amount} {(amount == 1 && economy.UsesStars ? "Star" : economy.CurrencyName)}";

        private static string Quantity(int amount, string singular) =>
            $"{amount} {(amount == 1 ? singular : singular + "s")}";

        private sealed class NightAmounts
        {
            internal readonly DuckPlayerState Player;
            internal readonly int FrozenSleep;
            private readonly DuckEconomyDefinition _economy;
            private readonly int _day, _printedSleep, _printedTwigs, _brambles, _flowers, _finalShelter, _restless,
                _eventSleep, _sunshineSleep, _flock, _pebbles, _beforeWear, _totalTwigs, _feathers;

            internal NightAmounts(int day, DuckPlayerState player, DuckRuleDefinitions rules,
                DuckWorldEventType worldEvent, int eventSleep, int greatestFlock)
            {
                Player = player;
                _economy = rules.Economy;
                _day = day;
                var final = player.PlacedChips.Last();
                if (final.Position != player.Position) throw new InvalidOperationException("Rest must use the final occupied space.");
                var finalChip = player.Inventory.Single(chip => chip.PhysicalChipId == final.PhysicalChipId);
                var finalType = rules.Encounter(finalChip.DefinitionId).EncounterType;
                var space = rules.BoardSpaceAt(player.Position);
                _printedSleep = space.Sleep;
                _printedTwigs = space.Twigs;
                var grossTwigs = space.Twigs + player.DayReedsTwigs + player.DayEventTwigs;
                _brambles = finalType == DuckEncounterType.Brambles && !final.NuisanceSuppressed ? Math.Min(1, grossTwigs) : 0;
                _totalTwigs = grossTwigs - _brambles;
                var shelterRewardsEligible = space.IsShelter && (rules.RulesRevision >= 5 || !player.IsWornOut);
                var economy = rules.Economy;
                _flowers = shelterRewardsEligible ? economy.CalculateFlowerReward(player.FlowersPlaced) : 0;
                _finalShelter = shelterRewardsEligible && day == DuckMatchSettings.StandardDays ? economy.FinalShelterReward : 0;
                var shelterSleep = space.Sleep + _flowers + _finalShelter;
                _restless = shelterRewardsEligible && worldEvent == DuckWorldEventType.RestlessNight
                    ? Math.Min(economy.RestlessNightPenalty, shelterSleep) : 0;
                _eventSleep = eventSleep;
                _sunshineSleep = worldEvent == DuckWorldEventType.GloriousSunshine
                    && player.HasGloriousSunshineChoice
                    && player.GloriousSunshineBenefit == DuckGloriousSunshineBenefit.WarmDreams ? economy.WarmDreamsReward : 0;
                var flockEligible = rules.RulesRevision >= 5 || !player.IsWornOut;
                _flock = flockEligible && player.ActiveFlock > 0 && player.ActiveFlock == greatestFlock
                    ? economy.CalculateFlockLeaderReward(player.ActiveFlock) : 0;
                var grossSleep = shelterSleep - _restless + _eventSleep + _sunshineSleep + _flock;
                _pebbles = finalType == DuckEncounterType.LoosePebbles && !final.NuisanceSuppressed
                    ? Math.Min(economy.PebblesPenalty, grossSleep) : 0;
                _beforeWear = grossSleep - _pebbles;
                FrozenSleep = player.IsWornOut ? _beforeWear / economy.WearOutDivisor : _beforeWear;
                _feathers = shelterRewardsEligible ? space.Feathers : 0;
            }

            internal DuckNightOutcome ToOutcome(bool mostRested) => new DuckNightOutcome(
                _day, _printedSleep, _printedTwigs, Player.DayReedsTwigs, Player.DayEventTwigs,
                _brambles, _flowers, _finalShelter, _restless, _eventSleep, _sunshineSleep, _flock, _pebbles,
                _beforeWear, FrozenSleep, _totalTwigs, _feathers, mostRested,
                mostRested && _day < DuckMatchSettings.StandardDays ? 1 : 0,
                _day == DuckMatchSettings.StandardDays
                    ? _economy.ConvertFinalRewardToDreamTwigs(FrozenSleep) + (mostRested ? 1 : 0)
                    : 0);
        }
    }

    /// <summary>One capability for every permanent Feather grant; never a spendable balance.</summary>
    internal static class DuckFeatherAwards
    {
        internal static void Give(DuckMatchState state, DuckPlayerState player, int amount, string source)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return;
            player.PermanentFeatherTrail += amount;
            state.PublicAwards.Add(new DuckPublicAwardState(state.Day, source, new[] { player.Id }, 0, 0, amount));
            state.History.Add(new DuckHistoryState(state.Day, player.Id,
                $"{player.Name} gains {amount} {(amount == 1 ? "Feather" : "Feathers")} from {source}."));
        }
    }
}
