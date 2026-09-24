using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.Runtime
{
    internal static class DuckDreamHandler
    {
        internal static int PurchaseLimitForDay(int day)
        {
            var nestLevel = NestLevelForDay(day);
            return day == DuckMatchSettings.StandardDays ? 0 : nestLevel;
        }

        internal static int NestLevelForDay(int day)
        {
            if (day < 1 || day > DuckMatchSettings.StandardDays) throw new ArgumentOutOfRangeException(nameof(day));
            return day <= 3 ? 1 : day <= 6 ? 2 : 3;
        }

        internal static IReadOnlyList<GameAction> GetLegalActions(DuckMatchState state, DuckPlayerState player, DuckRuleDefinitions rules)
        {
            var actions = new List<GameAction>();
            if (state.Phase != DuckPhase.Night || !player.IsSleepFrozen || player.HasFinishedDream || state.Day == 10)
                return DuckMatchView.Freeze(actions);

            if (player.PurchasedEncounterDefinitionIds.Count < PurchaseLimitForDay(state.Day))
            {
                actions.AddRange(rules.ShopOffers.Where(offer => offer.SleepPrice <= player.RemainingSleep && !player.PurchasedShopTypes.Contains(offer.ShopType))
                    .Select(offer => new GameAction("buy:" + offer.DefinitionId, GameActionKind.BuyEncounter,
                        $"Buy {(rules.Economy.UsesStars ? "Wish " : string.Empty)}{offer.Encounter.Name} for {Reward(offer.Price, rules.Economy)}",
                        cost: offer.Price, definitionId: offer.DefinitionId)));
            }
            actions.Add(new GameAction("finish-dream", GameActionKind.FinishDream, "Finish Dream choices"));
            return DuckMatchView.Freeze(actions);
        }

        internal static void Execute(DuckMatchState state, DuckPlayerState player, DuckRuleDefinitions rules, GameAction action)
        {
            var legal = GetLegalActions(state, player, rules).SingleOrDefault(candidate => candidate.Id == action.Id)
                ?? throw new InvalidOperationException("This Dream choice is no longer legal.");
            if (legal.Kind == GameActionKind.FinishDream)
            {
                player.HasFinishedDream = true;
                player.RemainingSleep = 0;
                state.History.Add(new DuckHistoryState(state.Day, player.Id,
                    $"{player.Name} finishes Dream choices; unspent {rules.Economy.CurrencyName} expire."));
                if (state.Players.All(candidate => candidate.HasFinishedDream)) state.Phase = DuckPhase.DayComplete;
                return;
            }

            var offer = rules.ShopOffer(legal.DefinitionId);
            player.RemainingSleep -= offer.Price;
            player.PurchasedEncounterDefinitionIds.Add(offer.DefinitionId);
            player.PurchasedShopTypes.Add(offer.ShopType);
            player.Inventory.Add(new DuckPhysicalChipState(state.NextPhysicalChipId++, offer.EncounterDefinitionId));
            state.History.Add(new DuckHistoryState(state.Day, player.Id,
                $"{player.Name} buys {(rules.Economy.UsesStars ? "Wish " : string.Empty)}{offer.Encounter.Name} for {Reward(offer.Price, rules.Economy)}; it enters tomorrow's pouch."));
        }

        private static string Reward(int amount, DuckEconomyDefinition economy) =>
            amount == 0 && economy.UsesStars
                ? "no Stars"
                : $"{amount} {(amount == 1 && economy.UsesStars ? "Star" : economy.CurrencyName)}";
    }
}
