using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Evaluation.Policies
{
    /// <summary>
    /// Preserved from the extracted M4 comparison baseline.
    /// Extracted source SHA-256: f8d0e7e26cf2152740c25b4666d0d94bdc1cf5fe8cdab55584bee15088a2f6ea.
    /// Mechanical adaptations are limited to namespace, class name, and the public evaluation decision DTO.
    /// A deterministic, bounded v1 opponent. It compares the current rest with one
    /// possible placement, using an exact Signpost preview when present and otherwise
    /// the visible remaining composition. It is intentionally a readable heuristic,
    /// not a second rules engine or a claim of optimal play.
    /// </summary>
    public sealed class M4BaselinePolicy : IDuckPlayerPolicy
    {
        public const string SourceSha256 = "f8d0e7e26cf2152740c25b4666d0d94bdc1cf5fe8cdab55584bee15088a2f6ea";
        private static readonly DuckRuleDefinitions Rules = DuckRules.V1;

        public GameAction Choose(DuckMatchView observation, IReadOnlyList<GameAction> legalActions) =>
            Evaluate(observation, legalActions).Action;

        public EvaluationPolicyDecision Evaluate(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (legalActions == null || legalActions.Count == 0)
                throw new ArgumentException("A policy needs at least one legal action.", nameof(legalActions));
            if (legalActions.Any(action => action == null))
                throw new ArgumentException("Legal actions cannot contain null.", nameof(legalActions));

            var player = observation.Players.Single(candidate => candidate.Id == observation.ViewerId);
            var explore = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.Explore);
            var settle = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.Settle);
            if (explore != null && settle != null)
                return ChooseAdventure(observation, player, explore, settle);
            if (explore != null)
                return new EvaluationPolicyDecision(explore, "The first placement is required before this duck may settle.");
            if (settle != null)
                return new EvaluationPolicyDecision(settle, "No further exploration action is available.");

            var buys = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter).ToArray();
            if (buys.Length > 0)
                return ChoosePurchase(observation, player, buys);

            var finishDream = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.FinishDream);
            if (finishDream != null)
                return new EvaluationPolicyDecision(finishDream, "No worthwhile affordable Dream purchase remains.");
            var nextDay = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.NextDay);
            if (nextDay != null)
                return new EvaluationPolicyDecision(nextDay, "Both ducks have finished Dream choices, so the next Day can begin.");

            return new EvaluationPolicyDecision(legalActions[0], "This is the only remaining supported action.");
        }

        private static EvaluationPolicyDecision ChooseAdventure(
            DuckMatchView observation,
            DuckPlayerView player,
            GameAction explore,
            GameAction settle)
        {
            var candidates = observation.KnownNextChips.Count > 0
                ? new[] { observation.KnownNextChips[0] }
                : observation.OwnBag.ToArray();
            if (candidates.Length == 0)
                return new EvaluationPolicyDecision(settle, "The bag is empty, so the current occupied space is final.");

            var estimates = candidates.Select(chip => EstimatePlacement(observation, player, chip)).ToArray();
            if (observation.KnownNextChips.Count > 0 && estimates[0].WearsOut)
                return new EvaluationPolicyDecision(settle,
                    $"The Signpost preview is {estimates[0].Name}, which would exceed Exhaustion {estimates[0].SafeMaximum}.");

            var currentValue = RestValue(observation, player, player.Position, player.ActiveFlock,
                player.FlowersPlaced, player.IsWornOut, CurrentFinalType(player), CurrentFinalSuppressed(player));
            var expectedValue = estimates.Average(estimate => estimate.RestValue);
            var wearChance = estimates.Count(estimate => estimate.WearsOut) / (double)estimates.Length;
            var leaderTwigs = observation.Players.Max(candidate => candidate.TotalTwigs);
            var deficit = Math.Max(0, leaderTwigs - player.TotalTwigs);
            var remainingDays = DuckMatchSettings.StandardDays - observation.Day;

            // A leader protects a good rest; a trailing duck accepts somewhat more
            // variance, especially late. Wear-out already loses safe-only rewards and
            // halves Sleep in each candidate estimate, so this is a modest extra cost.
            var caution = wearChance * (2.6 + remainingDays * 0.18);
            var catchUp = Math.Min(2.5, deficit * (observation.Day >= 7 ? 0.22 : 0.12));
            var currentSpace = Rules.BoardSpaceAt(player.Position);
            var continuation = wearChance == 0 && !currentSpace.IsShelter ? 0.55 : 0;
            var improvement = expectedValue - currentValue - caution + catchUp + continuation;
            if (improvement > 0.35)
            {
                var knowledge = observation.KnownNextChips.Count > 0
                    ? $"The known {estimates[0].Name}"
                    : $"The remaining bag averages {wearChance:P0} wear-out risk and";
                return new EvaluationPolicyDecision(explore,
                    $"{knowledge} improves the estimated rest value by {improvement:0.0} after risk and standings pressure.");
            }

            var shelter = currentSpace.IsShelter ? " safe shelter" : " space";
            return new EvaluationPolicyDecision(settle,
                $"Keeping{shelter} {player.Position} is worth more than the estimated next placement after {wearChance:P0} wear-out risk.");
        }

        private static PlacementEstimate EstimatePlacement(
            DuckMatchView observation,
            DuckPlayerView player,
            DuckPhysicalChipView chip)
        {
            var definition = Rules.Encounter(chip.DefinitionId);
            var movement = definition.EncounterType == DuckEncounterType.Companion
                ? Math.Min(player.ActiveFlock + 2, 4)
                : definition.BaseMovement!.Value;
            if (observation.CurrentEvent.EventType == DuckWorldEventType.RainSoftenedSeeds
                && definition.EncounterType == DuckEncounterType.Seeds)
                movement++;
            var stillAir = observation.CurrentEvent.EventType == DuckWorldEventType.StillAir
                && definition.EncounterType == DuckEncounterType.Tailwind;
            if (stillAir) movement = Halve(movement);
            if (definition.IsHelpful && player.LogSlowdownPending && !stillAir) movement = Halve(movement);

            var suppression = definition.IsObstacle
                && (player.SplashProtectionArmed || player.GuideProtectionAvailable);
            var exhaustion = player.Exhaustion + definition.ExhaustionValue;
            var safeMaximum = player.SafeExhaustionMaximum;
            if (definition.EncounterType == DuckEncounterType.GrumpyGoose && !suppression)
                safeMaximum = 4;
            var wearsOut = exhaustion > safeMaximum;
            var position = Math.Min(43, player.Position + movement);
            var flock = player.ActiveFlock;
            if (definition.EncounterType == DuckEncounterType.Companion) flock++;
            if (definition.EncounterType == DuckEncounterType.MudPuddle && !suppression)
                flock = Math.Max(0, flock - 1);
            var flowers = player.FlowersPlaced + (definition.EncounterType == DuckEncounterType.Wildflowers ? 1 : 0);
            var value = RestValue(observation, player, position, flock, flowers, wearsOut,
                definition.EncounterType, suppression);
            value += definition.TwigYield * 4.0;
            if (definition.EncounterType == DuckEncounterType.Splash)
                value += FutureObstacleFraction(observation) * 1.8;
            if (definition.EncounterType == DuckEncounterType.Signpost
                && observation.CurrentEvent.EventType != DuckWorldEventType.ThickMorningMist)
                value += 0.8;
            if (observation.CurrentEvent.EventType == DuckWorldEventType.PocketOfDriftwood
                && definition.IsHelpful
                && !player.PlacedHelpfulTypes.Contains(definition.EncounterType)
                && player.PlacedHelpfulTypes.Count == 2)
                value += 4.0;
            return new PlacementEstimate(definition.Name, safeMaximum, wearsOut, value);
        }

        private static double RestValue(
            DuckMatchView observation,
            DuckPlayerView player,
            int position,
            int activeFlock,
            int flowers,
            bool wornOut,
            DuckEncounterType? finalType,
            bool finalSuppressed)
        {
            var space = Rules.BoardSpaceAt(position);
            var safeShelter = space.IsShelter && !wornOut;
            var sleep = space.Sleep;
            if (safeShelter) sleep += flowers * 2;
            if (safeShelter && observation.Day == DuckMatchSettings.StandardDays) sleep += 2;
            if (safeShelter && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - 1);
            if (!wornOut && activeFlock > 0 && LeadsSafeFlock(observation, player.Id, activeFlock))
                sleep += Math.Min(2, activeFlock);
            sleep += CollectiveSleep(observation, player, position, wornOut, finalType);
            if (finalType == DuckEncounterType.LoosePebbles && !finalSuppressed)
                sleep = Math.Max(0, sleep - 1);
            if (wornOut) sleep /= 2;

            var twigs = space.Twigs;
            if (finalType == DuckEncounterType.Brambles && !finalSuppressed)
                twigs = Math.Max(0, twigs - 1);
            var remainingDays = DuckMatchSettings.StandardDays - observation.Day;
            var sleepWeight = observation.Day == DuckMatchSettings.StandardDays ? 1.0 : 0.38;
            var featherValue = safeShelter && remainingDays > 0
                ? space.Feathers * (0.8 + remainingDays * 0.55)
                : 0;
            var mostRestedValue = MostRestedValue(observation, player, sleep, wornOut);
            return twigs * 4.0 + sleep * sleepWeight + featherValue + mostRestedValue + position * 0.07;
        }

        private static double MostRestedValue(
            DuckMatchView observation,
            DuckPlayerView player,
            int estimatedSleep,
            bool wornOut)
        {
            if (wornOut) return 0;
            var eligibleOpponents = observation.Players
                .Where(candidate => candidate.Id != player.Id && !candidate.IsWornOut)
                .ToArray();
            if (eligibleOpponents.Length == 0)
                return observation.Day == DuckMatchSettings.StandardDays ? 4.0 : 1.4;
            var opponentSleep = eligibleOpponents.Max(candidate => candidate.IsSleepFrozen
                ? candidate.FrozenSleep
                : ProvisionalSleep(observation, candidate));
            if (estimatedSleep < opponentSleep) return 0;

            // Finished opponents provide an exact comparison after Night resolves;
            // an unfinished route supplies only a discounted public estimate.
            var confidence = eligibleOpponents.All(candidate => candidate.HasFinishedDay) ? 1.0 : 0.35;
            return observation.Day == DuckMatchSettings.StandardDays
                ? 4.0 * confidence // one final Dream Twig
                : 1.4 * confidence; // tomorrow's temporary one-step start
        }

        private static int ProvisionalSleep(DuckMatchView observation, DuckPlayerView player)
        {
            if (player.Position < 1) return 0;
            var space = Rules.BoardSpaceAt(player.Position);
            var safeShelter = space.IsShelter && !player.IsWornOut;
            var sleep = space.Sleep;
            if (safeShelter) sleep += player.FlowersPlaced * 2;
            if (safeShelter && observation.Day == DuckMatchSettings.StandardDays) sleep += 2;
            if (safeShelter && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - 1);
            if (!player.IsWornOut && player.ActiveFlock > 0
                && LeadsSafeFlock(observation, player.Id, player.ActiveFlock))
                sleep += Math.Min(2, player.ActiveFlock);
            sleep += CollectiveSleep(observation, player, player.Position, player.IsWornOut, CurrentFinalType(player));
            if (CurrentFinalType(player) == DuckEncounterType.LoosePebbles && !CurrentFinalSuppressed(player))
                sleep = Math.Max(0, sleep - 1);
            return player.IsWornOut ? sleep / 2 : sleep;
        }

        private static int CollectiveSleep(
            DuckMatchView observation,
            DuckPlayerView player,
            int position,
            bool wornOut,
            DuckEncounterType? finalType)
        {
            switch (observation.CurrentEvent.EventType)
            {
                case DuckWorldEventType.AllTuckedIn:
                    return !wornOut && Rules.BoardSpaceAt(position).IsShelter
                        && OthersFinished(observation, player.Id, other => !other.IsWornOut && Rules.BoardSpaceAt(other.Position).IsShelter)
                        ? 2 : 0;
                case DuckWorldEventType.HomeBeforeDark:
                    return !wornOut && OthersFinished(observation, player.Id, other => !other.IsWornOut) ? 1 : 0;
                case DuckWorldEventType.SharedSupper:
                    return (player.PlacedHelpfulTypes.Contains(DuckEncounterType.Seeds)
                            || finalType == DuckEncounterType.Seeds)
                        && OthersFinished(observation, player.Id,
                            other => other.PlacedHelpfulTypes.Contains(DuckEncounterType.Seeds)) ? 1 : 0;
                default:
                    return 0;
            }
        }

        private static bool OthersFinished(
            DuckMatchView observation,
            string playerId,
            Func<DuckPlayerView, bool> condition)
        {
            var others = observation.Players.Where(player => player.Id != playerId).ToArray();
            return others.All(player => player.HasFinishedDay && condition(player));
        }

        private static bool LeadsSafeFlock(DuckMatchView observation, string playerId, int activeFlock)
        {
            return observation.Players.Where(player => player.Id != playerId && !player.IsWornOut)
                .All(player => activeFlock >= player.ActiveFlock);
        }

        private static DuckEncounterType? CurrentFinalType(DuckPlayerView player)
        {
            var final = player.PlacedChips.LastOrDefault();
            return final == null ? (DuckEncounterType?)null : Rules.Encounter(final.DefinitionId).EncounterType;
        }

        private static bool CurrentFinalSuppressed(DuckPlayerView player) =>
            player.PlacedChips.LastOrDefault()?.NuisanceSuppressed ?? false;

        private static double FutureObstacleFraction(DuckMatchView observation)
        {
            if (observation.OwnBag.Count == 0) return 0;
            return observation.OwnBag.Count(chip => Rules.Encounter(chip.DefinitionId).IsObstacle)
                / (double)observation.OwnBag.Count;
        }

        private static EvaluationPolicyDecision ChoosePurchase(
            DuckMatchView observation,
            DuckPlayerView player,
            IReadOnlyList<GameAction> buys)
        {
            var scored = buys.Select(action =>
            {
                var offer = Rules.ShopOffer(action.DefinitionId);
                return new { Action = action, Offer = offer, Score = PurchaseValue(observation, player, offer) };
            }).OrderByDescending(choice => choice.Score)
              .ThenBy(choice => choice.Offer.SleepPrice)
              .ThenBy(choice => choice.Offer.DefinitionId, StringComparer.Ordinal)
              .ToArray();
            var best = scored[0];
            return new EvaluationPolicyDecision(best.Action,
                $"{best.Offer.Encounter.Name} gives the best remaining-Day value ({best.Score:0.0}) for {best.Offer.SleepPrice} Sleep.");
        }

        private static double PurchaseValue(
            DuckMatchView observation,
            DuckPlayerView player,
            DuckShopOffer offer)
        {
            var definition = offer.Encounter;
            var usableDays = DuckMatchSettings.StandardDays - observation.Day;
            var movement = definition.EncounterType == DuckEncounterType.Companion
                ? 2.5
                : definition.BaseMovement!.Value;
            var movementValue = movement * (1.0 + usableDays * 0.12);
            var twigValue = definition.TwigYield * (observation.Day >= 7 ? 5.0 : 2.0);
            var abilityValue = definition.EncounterType switch
            {
                DuckEncounterType.Signpost => 1.3,
                DuckEncounterType.Splash => 2.0,
                DuckEncounterType.Companion => 1.7,
                DuckEncounterType.Wildflowers => 1.2,
                DuckEncounterType.Seeds => 1.0,
                _ => 0.0
            };
            var ownedTypeCount = observation.OwnInventory.Count(chip =>
                Rules.Encounter(chip.DefinitionId).EncounterType == definition.EncounterType);
            var diversityAdjustment = ownedTypeCount == 0 ? 0.7 : -0.25 * ownedTypeCount;
            return movementValue + twigValue + abilityValue + diversityAdjustment - offer.SleepPrice * 0.18;
        }

        private static int Halve(int movement) => Math.Max(1, (movement + 1) / 2);

        private sealed class PlacementEstimate
        {
            internal PlacementEstimate(string name, int safeMaximum, bool wearsOut, double restValue)
            {
                Name = name;
                SafeMaximum = safeMaximum;
                WearsOut = wearsOut;
                RestValue = restValue;
            }

            internal string Name { get; }
            internal int SafeMaximum { get; }
            internal bool WearsOut { get; }
            internal double RestValue { get; }
        }
    }
}
