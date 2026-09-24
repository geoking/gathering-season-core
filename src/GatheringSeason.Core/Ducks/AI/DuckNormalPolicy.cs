using System;
using System.Collections.Generic;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.AI
{
    /// <summary>
    /// A deterministic, bounded v1 opponent. It compares the current rest with
    /// contingent multi-draw plans, using exact Signpost previews when present and
    /// otherwise only the visible remaining composition. It is intentionally a
    /// readable heuristic, not a claim of optimal play.
    /// </summary>
    public sealed class DuckNormalPolicy : IDuckPlayerPolicy
    {
        private const int MaximumPlanningDepth = 6;
        private const int MaximumPlanningNodes = 8000;
        private const double ProvableFinalLossPenalty = 1000.0;
        public GameAction Choose(DuckMatchView observation, IReadOnlyList<GameAction> legalActions) =>
            Evaluate(observation, legalActions).Action;

        public DuckPolicyDecision Evaluate(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (legalActions == null || legalActions.Count == 0)
                throw new ArgumentException("A policy needs at least one legal action.", nameof(legalActions));
            if (legalActions.Any(action => action == null))
                throw new ArgumentException("Legal actions cannot contain null.", nameof(legalActions));

            var player = observation.Players.Single(candidate => candidate.Id == observation.ViewerId);
            var eventBenefits = legalActions.Where(action => action.Kind == GameActionKind.ChooseEventBenefit).ToArray();
            if (eventBenefits.Length > 0)
            {
                var chooseWarmDreams = observation.Day >= 7;
                var choice = eventBenefits.Single(action => action.Id.EndsWith(
                    chooseWarmDreams ? "warm-dreams" : "fresh-air", StringComparison.Ordinal));
                return new DuckPolicyDecision(choice, chooseWarmDreams
                    ? $"Late in the season, {observation.Economy.WarmDreamsReward} {observation.Economy.CurrencyName} are valuable for purchases, Most Rested and the final conversion."
                    : $"Early in the season, {observation.Rules.FreshAirExhaustionBonus} extra safe Exhaustion supports a longer route and stronger shelter reward.");
            }
            var explore = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.Explore);
            var settle = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.Settle);
            if (explore != null && settle != null)
                return ChooseAdventure(observation, player, explore, settle);
            if (explore != null)
                return new DuckPolicyDecision(explore, "The first placement is required before this duck may settle.");
            if (settle != null)
                return new DuckPolicyDecision(settle, "No further exploration action is available.");

            var buys = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter).ToArray();
            var finishDream = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.FinishDream);
            if (buys.Length > 0)
                return ChoosePurchase(observation, player, buys, finishDream!);

            if (finishDream != null)
                return new DuckPolicyDecision(finishDream, "No worthwhile affordable Dream purchase remains.");
            var nextDay = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.NextDay);
            if (nextDay != null)
                return new DuckPolicyDecision(nextDay, "Both ducks have finished Dream choices, so the next Day can begin.");

            return new DuckPolicyDecision(legalActions[0], "This is the only remaining supported action.");
        }

        private static DuckPolicyDecision ChooseAdventure(
            DuckMatchView observation,
            DuckPlayerView player,
            GameAction explore,
            GameAction settle)
        {
            if (observation.OwnBag.Count == 0)
                return new DuckPolicyDecision(settle, "The pouch is empty, so the current occupied space is final.");

            var plan = PlanAdventure(observation, player);
            if (plan.KnownFirstWearsOut && observation.RulesRevision < 5)
                return new DuckPolicyDecision(settle,
                    $"The Signpost preview is {plan.KnownFirstName}, which would exceed Exhaustion {plan.KnownFirstSafeMaximum}.");
            if (plan.CurrentRestProvablyLoses && plan.OptimisticRecoveryPossible)
                return new DuckPolicyDecision(explore,
                    $"The current final score is provably behind; continuing keeps a possible recovery open beyond the " +
                    $"completed {plan.CompletedDepth}-draw horizon ({plan.TotalNodes} total nodes).");

            var leaderTwigs = observation.Players.Max(candidate => candidate.TotalTwigs);
            var deficit = Math.Max(0, leaderTwigs - player.TotalTwigs);
            var catchUp = Math.Min(2.5, deficit * (observation.Day >= 7 ? 0.22 : 0.12));
            var currentSpace = observation.Rules.BoardSpaceAt(player.Position);
            var lowRiskOnwardTravel = plan.ImmediateWearChance == 0 && !currentSpace.IsShelter ? 0.45 : 0;
            var improvement = plan.ExploreValue - plan.CurrentValue + catchUp + lowRiskOnwardTravel;
            if (improvement > 0.35)
            {
                var knowledge = plan.CurrentRestProvablyLoses
                    ? "The current final score is provably behind; uncertain continuation"
                    : observation.KnownNextChips.Count > 0
                        ? $"The known {plan.KnownFirstName}"
                        : $"The remaining pouch has {plan.ImmediateWearChance:P0} immediate wear-out risk and";
                return new DuckPolicyDecision(explore,
                    $"{knowledge} improves the {plan.CompletedDepth}-draw plan by {improvement:0.0}; " +
                    $"the search used {plan.TotalNodes} nodes ({plan.CompletedNodes} at that depth) and may settle after each revealed draw.");
            }

            var shelter = currentSpace.IsShelter ? " safe shelter" : " space";
            return new DuckPolicyDecision(settle,
                $"Keeping{shelter} {player.Position} is worth more than the completed {plan.CompletedDepth}-draw plan " +
                $"after {plan.ImmediateWearChance:P0} immediate wear-out risk ({plan.TotalNodes} total nodes).");
        }

        private static AdventurePlan PlanAdventure(DuckMatchView observation, DuckPlayerView player)
        {
            var definitions = observation.Rules.EncounterDefinitions;
            var counts = new int[definitions.Count];
            foreach (var chip in observation.OwnBag)
                counts[DefinitionIndex(observation, chip.DefinitionId)]++;
            var known = observation.KnownNextChips.Select(chip => DefinitionIndex(observation, chip.DefinitionId)).ToArray();
            var state = new PlannedAdventureState(
                new DuckAdventureState(
                    player.Position,
                    player.Exhaustion,
                    player.SafeExhaustionMaximum,
                    player.ActiveFlock,
                    player.SplashProtectionArmed,
                    player.LogSlowdownPending,
                    player.GuideProtectionAvailable,
                    observation.CurrentEvent.EventType == DuckWorldEventType.PocketOfDriftwood
                        && player.DayEventTwigs > 0,
                    player.FlowersPlaced,
                    DuckAdventureRules.HelpfulTypes(player.PlacedHelpfulTypes)),
                ScoringFinalType(CurrentFinalType(observation, player)),
                ScoringFinalType(CurrentFinalType(observation, player)).HasValue && CurrentFinalSuppressed(player),
                futureTwigs: 0,
                wornOut: player.IsWornOut);

            var currentValue = PlannedRestValue(observation, player, state);
            var acceptedValue = currentValue;
            var acceptedDepth = 0;
            var acceptedNodes = 0;
            var maximumDepth = Math.Min(MaximumPlanningDepth, observation.OwnBag.Count);
            var search = new AdventureSearch(observation, player, MaximumPlanningNodes);
            for (var depth = 1; depth <= maximumDepth; depth++)
            {
                var nodesBeforeDepth = search.Nodes;
                try
                {
                    var value = search.DrawValue(state, counts, known, depth);
                    acceptedValue = value;
                    acceptedDepth = depth;
                    acceptedNodes = search.Nodes - nodesBeforeDepth;
                }
                catch (PlanningLimitExceededException)
                {
                    break;
                }
            }

            var firstCandidates = known.Length > 0
                ? new[] { known[0] }
                : Enumerable.Range(0, counts.Length).Where(index => counts[index] > 0).ToArray();
            var firstWearWeight = 0;
            var firstWeight = 0;
            foreach (var index in firstCandidates)
            {
                var weight = known.Length > 0 ? 1 : counts[index];
                var placement = DuckAdventureRules.ApplyEncounter(
                    state.State,
                    definitions[index],
                    observation.CurrentEvent.EventType);
                firstWeight += weight;
                if (placement.WearsOut) firstWearWeight += weight;
            }

            var knownFirst = known.Length == 0
                ? (DuckAdventurePlacement?)null
                : DuckAdventureRules.ApplyEncounter(state.State, definitions[known[0]], observation.CurrentEvent.EventType);
            var currentRestProvablyLoses = IsProvablyLosingFinalRest(observation, player, state);
            return new AdventurePlan(
                currentValue,
                acceptedValue,
                firstWeight == 0 ? 0 : firstWearWeight / (double)firstWeight,
                acceptedDepth,
                acceptedNodes,
                search.Nodes,
                currentRestProvablyLoses,
                currentRestProvablyLoses && OptimisticFinalRecoveryPossible(observation, player, state, counts),
                knownFirst?.WearsOut ?? false,
                known.Length == 0 ? string.Empty : definitions[known[0]].Name,
                knownFirst?.State.SafeExhaustionMaximum ?? state.State.SafeExhaustionMaximum);
        }

        private static int DefinitionIndex(DuckMatchView observation, string definitionId)
        {
            for (var index = 0; index < observation.Rules.EncounterDefinitions.Count; index++)
                if (string.Equals(observation.Rules.EncounterDefinitions[index].DefinitionId, definitionId, StringComparison.Ordinal))
                    return index;
            throw new InvalidOperationException("The observed pouch contains an unknown encounter definition: " + definitionId);
        }

        private static double PlannedRestValue(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState state)
        {
            var value = RestValue(
                observation,
                player,
                state.State.Position,
                state.State.ActiveFlock,
                state.State.FlowersPlaced,
                state.WornOut,
                state.FinalType,
                state.FinalSuppressed,
                state.State.HelpfulTypeMask);
            value += state.FutureTwigs * 4.0;
            if (state.WornOut)
            {
                var remainingDays = DuckMatchSettings.StandardDays - observation.Day;
                value -= 2.6 + remainingDays * 0.18;
            }
            if (IsProvablyLosingFinalRest(observation, player, state))
                value -= ProvableFinalLossPenalty;
            return value;
        }

        private static bool IsProvablyLosingFinalRest(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState state)
        {
            if (observation.Day != DuckMatchSettings.StandardDays) return false;
            var ownUpper = OwnFinalUpperBound(observation, player, state);
            return observation.Players
                .Where(opponent => opponent.Id != player.Id)
                .Select(opponent => OpponentFinalLowerBound(observation, opponent))
                .Any(opponentLower => opponentLower.Twigs > ownUpper.Twigs
                    || opponentLower.Twigs == ownUpper.Twigs && opponentLower.Sleep > ownUpper.Sleep);
        }

        private static bool OptimisticFinalRecoveryPossible(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState current,
            IReadOnlyList<int> counts)
        {
            if (observation.Day != DuckMatchSettings.StandardDays) return false;

            // This is an upper bound, not a feasible route or a second simulation.
            // Ignore nuisances/exhaustion and credit every remaining helpful chip;
            // it is used only when the current rest is already certain to lose.
            // A positive result keeps an uncertain recovery open beyond the search
            // horizon; it never promises that the required draw order is possible.
            var movement = 0;
            var reedsTwigs = 0;
            var flowers = current.State.FlowersPlaced;
            var flock = current.State.ActiveFlock;
            var helpfulTypes = player.PlacedHelpfulTypes.ToList();
            for (var index = 0; index < counts.Count; index++)
            {
                var definition = observation.Rules.EncounterDefinitions[index];
                var count = counts[index];
                if (count == 0) continue;
                movement += count * (definition.EncounterType == DuckEncounterType.Companion
                    ? 4
                    : definition.BaseMovement!.Value);
                if (observation.CurrentEvent.EventType == DuckWorldEventType.RainSoftenedSeeds
                    && definition.EncounterType == DuckEncounterType.Seeds)
                    movement += count;
                reedsTwigs += count * definition.TwigYield;
                if (definition.EncounterType == DuckEncounterType.Wildflowers) flowers += count;
                if (definition.EncounterType == DuckEncounterType.Companion) flock += count;
                if (definition.IsHelpful) helpfulTypes.Add(definition.EncounterType);
            }

            var futureTwigs = current.FutureTwigs + reedsTwigs;
            if (observation.CurrentEvent.EventType == DuckWorldEventType.PocketOfDriftwood
                && !current.State.PocketDriftwoodAwarded)
                futureTwigs++;
            var maximumPosition = Math.Min(43, current.State.Position + movement);
            var opponentBounds = observation.Players
                .Where(opponent => opponent.Id != player.Id)
                .Select(opponent => OpponentFinalLowerBound(observation, opponent))
                .ToArray();

            return observation.Rules.BoardSpaces
                .Where(space => space.Space >= current.State.Position && space.Space <= maximumPosition)
                .Select(space => new PlannedAdventureState(
                    new DuckAdventureState(
                        space.Space,
                        current.State.Exhaustion,
                        current.State.SafeExhaustionMaximum,
                        flock,
                        splashProtectionArmed: true,
                        logSlowdownPending: false,
                        current.State.GuideProtectionAvailable,
                        pocketDriftwoodAwarded: true,
                        flowers,
                        DuckAdventureRules.HelpfulTypes(helpfulTypes)),
                    finalType: null,
                    finalSuppressed: false,
                    futureTwigs,
                    wornOut: false))
                .Select(state => OwnFinalUpperBound(observation, player, state))
                .Any(ownUpper => opponentBounds.All(opponentLower =>
                    ownUpper.Twigs > opponentLower.Twigs
                    || ownUpper.Twigs == opponentLower.Twigs && ownUpper.Sleep >= opponentLower.Sleep));
        }

        private static FinalScoreBound OwnFinalUpperBound(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState state)
        {
            var space = observation.Rules.BoardSpaceAt(state.State.Position);
            var brambles = state.FinalType == DuckEncounterType.Brambles && !state.FinalSuppressed ? 1 : 0;
            var sleep = FinalSleepUpperBound(observation, player, state, space);
            var mostRestedTwig = state.WornOut ? 0 : 1;
            var twigs = player.TotalTwigs + state.FutureTwigs + space.Twigs - brambles
                + observation.Economy.ConvertFinalRewardToDreamTwigs(sleep) + mostRestedTwig;
            return new FinalScoreBound(twigs, sleep);
        }

        private static int FinalSleepUpperBound(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState state,
            DuckBoardSpace space)
        {
            var shelterRewardsEligible = ShelterRewardsEligible(observation, space, state.WornOut);
            var sleep = space.Reward;
            if (shelterRewardsEligible) sleep += observation.Economy.CalculateFlowerReward(state.State.FlowersPlaced)
                + observation.Economy.FinalShelterReward;
            if (shelterRewardsEligible && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - observation.Economy.RestlessNightPenalty);
            sleep += CollectiveSleepUpperBound(observation, player, state, shelterRewardsEligible);
            sleep += GloriousSunshineReward(observation, player);
            if (FlockRewardsEligible(observation, state.WornOut) && state.State.ActiveFlock > 0
                && observation.Players.Where(opponent => opponent.Id != player.Id && opponent.HasFinishedDay
                    && (observation.RulesRevision >= 5 || !opponent.IsWornOut))
                    .All(opponent => state.State.ActiveFlock >= opponent.ActiveFlock))
                sleep += observation.Economy.CalculateFlockLeaderReward(state.State.ActiveFlock);
            if (state.FinalType == DuckEncounterType.LoosePebbles && !state.FinalSuppressed)
                sleep = Math.Max(0, sleep - observation.Economy.PebblesPenalty);
            return state.WornOut ? sleep / observation.Economy.WearOutDivisor : sleep;
        }

        private static int CollectiveSleepUpperBound(
            DuckMatchView observation,
            DuckPlayerView player,
            PlannedAdventureState state,
            bool shelterRewardsEligible)
        {
            var opponents = observation.Players.Where(opponent => opponent.Id != player.Id).ToArray();
            switch (observation.CurrentEvent.EventType)
            {
                case DuckWorldEventType.AllTuckedIn:
                    return shelterRewardsEligible && opponents.All(opponent => !opponent.HasFinishedDay
                        || (observation.RulesRevision >= 5 || !opponent.IsWornOut)
                        && observation.Rules.BoardSpaceAt(opponent.Position).IsShelter)
                        ? observation.Economy.AllTuckedInReward : 0;
                case DuckWorldEventType.HomeBeforeDark:
                    return !state.WornOut && opponents.All(opponent => !opponent.HasFinishedDay || !opponent.IsWornOut)
                        ? observation.Economy.HomeBeforeDarkReward : 0;
                case DuckWorldEventType.SharedSupper:
                    return DuckAdventureRules.ContainsHelpfulType(state.State.HelpfulTypeMask, DuckEncounterType.Seeds)
                        && opponents.All(opponent => !opponent.HasFinishedDay
                            || opponent.PlacedHelpfulTypes.Contains(DuckEncounterType.Seeds))
                        ? observation.Economy.SharedSupperReward : 0;
                default:
                    return 0;
            }
        }

        private static FinalScoreBound OpponentFinalLowerBound(
            DuckMatchView observation,
            DuckPlayerView opponent)
        {
            if (opponent.Position < 1)
                return new FinalScoreBound(Math.Max(0, opponent.TotalTwigs - 1), 0);

            var fixedRest = opponent.HasFinishedDay;
            var printedTwigs = fixedRest
                ? observation.Rules.BoardSpaceAt(opponent.Position).Twigs
                : observation.Rules.BoardSpaces.Where(space => space.Space >= opponent.Position).Min(space => space.Twigs);
            var finalType = CurrentFinalType(observation, opponent);
            var brambles = fixedRest
                ? finalType == DuckEncounterType.Brambles && !CurrentFinalSuppressed(opponent) ? 1 : 0
                : 1;
            var sleep = fixedRest ? FinishedOpponentSleepLowerBound(observation, opponent) : 0;
            var twigs = Math.Max(0, opponent.TotalTwigs + printedTwigs - brambles)
                + observation.Economy.ConvertFinalRewardToDreamTwigs(sleep);
            return new FinalScoreBound(twigs, sleep);
        }

        private static int FinishedOpponentSleepLowerBound(
            DuckMatchView observation,
            DuckPlayerView opponent)
        {
            var space = observation.Rules.BoardSpaceAt(opponent.Position);
            var shelterRewardsEligible = ShelterRewardsEligible(observation, space, opponent.IsWornOut);
            var sleep = space.Reward;
            if (shelterRewardsEligible) sleep += observation.Economy.CalculateFlowerReward(opponent.FlowersPlaced)
                + observation.Economy.FinalShelterReward;
            if (shelterRewardsEligible && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - observation.Economy.RestlessNightPenalty);
            sleep += GloriousSunshineReward(observation, opponent);
            if (CurrentFinalType(observation, opponent) == DuckEncounterType.LoosePebbles && !CurrentFinalSuppressed(opponent))
                sleep = Math.Max(0, sleep - observation.Economy.PebblesPenalty);
            return opponent.IsWornOut ? sleep / observation.Economy.WearOutDivisor : sleep;
        }

        private static double RestValue(
            DuckMatchView observation,
            DuckPlayerView player,
            int position,
            int activeFlock,
            int flowers,
            bool wornOut,
            DuckEncounterType? finalType,
            bool finalSuppressed,
            int helpfulTypeMask)
        {
            var space = observation.Rules.BoardSpaceAt(position);
            var shelterRewardsEligible = ShelterRewardsEligible(observation, space, wornOut);
            var sleep = space.Reward;
            if (shelterRewardsEligible) sleep += observation.Economy.CalculateFlowerReward(flowers);
            if (shelterRewardsEligible && observation.Day == DuckMatchSettings.StandardDays)
                sleep += observation.Economy.FinalShelterReward;
            if (shelterRewardsEligible && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - observation.Economy.RestlessNightPenalty);
            if (FlockRewardsEligible(observation, wornOut) && activeFlock > 0
                && LeadsFlock(observation, player.Id, activeFlock))
                sleep += observation.Economy.CalculateFlockLeaderReward(activeFlock);
            sleep += CollectiveSleep(observation, player, position, wornOut, finalType, helpfulTypeMask);
            sleep += GloriousSunshineReward(observation, player);
            if (finalType == DuckEncounterType.LoosePebbles && !finalSuppressed)
                sleep = Math.Max(0, sleep - observation.Economy.PebblesPenalty);
            if (wornOut) sleep /= observation.Economy.WearOutDivisor;

            var twigs = space.Twigs;
            if (finalType == DuckEncounterType.Brambles && !finalSuppressed)
                twigs = Math.Max(0, twigs - 1);
            var remainingDays = DuckMatchSettings.StandardDays - observation.Day;
            var rewardScale = observation.Economy.UsesStars ? 4.0 : 1.0;
            var sleepWeight = (observation.Day == DuckMatchSettings.StandardDays ? 1.0 : 0.38) * rewardScale;
            var featherValue = shelterRewardsEligible && remainingDays > 0
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
            var space = observation.Rules.BoardSpaceAt(player.Position);
            var shelterRewardsEligible = ShelterRewardsEligible(observation, space, player.IsWornOut);
            var sleep = space.Reward;
            if (shelterRewardsEligible) sleep += observation.Economy.CalculateFlowerReward(player.FlowersPlaced);
            if (shelterRewardsEligible && observation.Day == DuckMatchSettings.StandardDays)
                sleep += observation.Economy.FinalShelterReward;
            if (shelterRewardsEligible && observation.CurrentEvent.EventType == DuckWorldEventType.RestlessNight)
                sleep = Math.Max(0, sleep - observation.Economy.RestlessNightPenalty);
            if (FlockRewardsEligible(observation, player.IsWornOut) && player.ActiveFlock > 0
                && LeadsFlock(observation, player.Id, player.ActiveFlock))
                sleep += observation.Economy.CalculateFlockLeaderReward(player.ActiveFlock);
            sleep += CollectiveSleep(
                observation,
                player,
                player.Position,
                player.IsWornOut,
                CurrentFinalType(observation, player),
                DuckAdventureRules.HelpfulTypes(player.PlacedHelpfulTypes));
            sleep += GloriousSunshineReward(observation, player);
            if (CurrentFinalType(observation, player) == DuckEncounterType.LoosePebbles && !CurrentFinalSuppressed(player))
                sleep = Math.Max(0, sleep - observation.Economy.PebblesPenalty);
            return player.IsWornOut ? sleep / observation.Economy.WearOutDivisor : sleep;
        }

        private static int CollectiveSleep(
            DuckMatchView observation,
            DuckPlayerView player,
            int position,
            bool wornOut,
            DuckEncounterType? finalType,
            int helpfulTypeMask)
        {
            switch (observation.CurrentEvent.EventType)
            {
                case DuckWorldEventType.AllTuckedIn:
                    return (observation.RulesRevision >= 5 || !wornOut)
                        && observation.Rules.BoardSpaceAt(position).IsShelter
                        && OthersFinished(observation, player.Id, other =>
                            (observation.RulesRevision >= 5 || !other.IsWornOut)
                            && observation.Rules.BoardSpaceAt(other.Position).IsShelter)
                        ? observation.Economy.AllTuckedInReward : 0;
                case DuckWorldEventType.HomeBeforeDark:
                    return !wornOut && OthersFinished(observation, player.Id, other => !other.IsWornOut)
                        ? observation.Economy.HomeBeforeDarkReward : 0;
                case DuckWorldEventType.SharedSupper:
                    return DuckAdventureRules.ContainsHelpfulType(helpfulTypeMask, DuckEncounterType.Seeds)
                        && OthersFinished(observation, player.Id,
                            other => other.PlacedHelpfulTypes.Contains(DuckEncounterType.Seeds))
                        ? observation.Economy.SharedSupperReward : 0;
                default:
                    return 0;
            }
        }

        private static int GloriousSunshineReward(DuckMatchView observation, DuckPlayerView player) =>
            player.HasGloriousSunshineChoice
            && player.GloriousSunshineBenefit == DuckGloriousSunshineBenefit.WarmDreams
                ? observation.Economy.WarmDreamsReward : 0;

        private static bool OthersFinished(
            DuckMatchView observation,
            string playerId,
            Func<DuckPlayerView, bool> condition)
        {
            var others = observation.Players.Where(player => player.Id != playerId).ToArray();
            return others.All(player => player.HasFinishedDay && condition(player));
        }

        private static bool LeadsFlock(DuckMatchView observation, string playerId, int activeFlock)
        {
            return observation.Players.Where(player => player.Id != playerId
                    && (observation.RulesRevision >= 5 || !player.IsWornOut))
                .All(player => activeFlock >= player.ActiveFlock);
        }

        private static bool ShelterRewardsEligible(
            DuckMatchView observation,
            DuckBoardSpace space,
            bool wornOut) =>
            space.IsShelter && (observation.RulesRevision >= 5 || !wornOut);

        private static bool FlockRewardsEligible(DuckMatchView observation, bool wornOut) =>
            observation.RulesRevision >= 5 || !wornOut;

        private static DuckEncounterType? CurrentFinalType(DuckMatchView observation, DuckPlayerView player)
        {
            var final = player.PlacedChips.LastOrDefault();
            return final == null ? (DuckEncounterType?)null : observation.Rules.Encounter(final.DefinitionId).EncounterType;
        }

        private static bool CurrentFinalSuppressed(DuckPlayerView player) =>
            player.PlacedChips.LastOrDefault()?.NuisanceSuppressed ?? false;

        private static DuckEncounterType? ScoringFinalType(DuckEncounterType? type) =>
            type == DuckEncounterType.Brambles || type == DuckEncounterType.LoosePebbles ? type : null;

        private static DuckPolicyDecision ChoosePurchase(
            DuckMatchView observation,
            DuckPlayerView player,
            IReadOnlyList<GameAction> buys,
            GameAction finishDream)
        {
            var observedOffers = observation.ShopOffers.ToDictionary(
                offer => offer.DefinitionId, StringComparer.Ordinal);
            var purchased = player.PurchasedEncounterDefinitionIds
                .Select(id => observedOffers[id])
                .ToArray();
            var remainingSlots = Math.Max(0, player.PurchaseLimit - purchased.Length);
            if (remainingSlots == 0)
                return new DuckPolicyDecision(finishDream, "The Nest has no open purchase slot, so Dream choices are complete.");

            var actionsByDefinition = buys.ToDictionary(action => action.DefinitionId, StringComparer.Ordinal);
            var candidates = buys.Select(action => observedOffers[action.DefinitionId])
                .OrderBy(offer => offer.DefinitionId, StringComparer.Ordinal)
                .ToArray();
            var completions = EnumerateBundles(candidates, player.RemainingReward, remainingSlots)
                .Select(extension =>
                {
                    var complete = purchased.Concat(extension).ToArray();
                    return new PurchaseBundle(
                        extension,
                        complete,
                        PurchaseBundleValue(observation, player, complete));
                })
                .OrderByDescending(bundle => bundle.Value)
                .ThenByDescending(bundle => bundle.Extension.Count)
                .ThenBy(bundle => bundle.Extension.Sum(offer => offer.Price))
                .ThenBy(bundle => BundleKey(bundle.Extension), StringComparer.Ordinal)
                .ToArray();

            var best = completions[0];
            if (best.Extension.Count == 0)
                return new DuckPolicyDecision(finishDream,
                    $"The complete {best.Complete.Count}-chip Night bundle is strongest without another purchase ({best.Value:0.0} value).");

            var next = best.Extension
                .OrderByDescending(offer => PurchaseValue(observation, player, offer, best.Complete))
                .ThenBy(offer => offer.Price)
                .ThenBy(offer => offer.DefinitionId, StringComparer.Ordinal)
                .First();
            return new DuckPolicyDecision(actionsByDefinition[next.DefinitionId],
                $"{(observation.Economy.UsesStars ? "Wish " : string.Empty)}{next.Encounter.Name} starts the best affordable {best.Complete.Count}-chip Night bundle " +
                $"({best.Value:0.0} value, {best.Extension.Sum(offer => offer.Price)} remaining {observation.Economy.CurrencyName} spend).");
        }

        private static IEnumerable<IReadOnlyList<DuckShopOffer>> EnumerateBundles(
            IReadOnlyList<DuckShopOffer> candidates,
            int budget,
            int maximumCount)
        {
            var bundle = new List<DuckShopOffer>();
            foreach (var result in EnumerateFrom(0, budget)) yield return result;

            IEnumerable<IReadOnlyList<DuckShopOffer>> EnumerateFrom(int index, int remainingBudget)
            {
                yield return bundle.ToArray();
                if (bundle.Count == maximumCount) yield break;

                for (var candidateIndex = index; candidateIndex < candidates.Count; candidateIndex++)
                {
                    var candidate = candidates[candidateIndex];
                    if (candidate.Price > remainingBudget
                        || bundle.Any(offer => offer.ShopType == candidate.ShopType))
                        continue;

                    bundle.Add(candidate);
                    foreach (var result in EnumerateFrom(candidateIndex + 1, remainingBudget - candidate.Price))
                        yield return result;
                    bundle.RemoveAt(bundle.Count - 1);
                }
            }
        }

        private static double PurchaseBundleValue(
            DuckMatchView observation,
            DuckPlayerView player,
            IReadOnlyList<DuckShopOffer> completeBundle)
        {
            // OwnInventory already contains purchases made earlier this Dream. Remove
            // those copies before scoring the complete Night bundle so each call sees
            // the same portfolio and does not value earlier choices twice.
            var beforeDream = observation.OwnInventory
                .Select(chip => chip.DefinitionId)
                .ToList();
            foreach (var purchased in player.PurchasedEncounterDefinitionIds)
                beforeDream.Remove(purchased);

            return completeBundle.Sum(offer => PurchaseValue(observation, player, offer, completeBundle, beforeDream));
        }

        private static double PurchaseValue(
            DuckMatchView observation,
            DuckPlayerView player,
            DuckShopOffer offer,
            IReadOnlyList<DuckShopOffer> completeBundle)
        {
            var beforeDream = observation.OwnInventory
                .Select(chip => chip.DefinitionId)
                .ToList();
            foreach (var purchased in player.PurchasedEncounterDefinitionIds)
                beforeDream.Remove(purchased);
            return PurchaseValue(observation, player, offer, completeBundle, beforeDream);
        }

        private static double PurchaseValue(
            DuckMatchView observation,
            DuckPlayerView player,
            DuckShopOffer offer,
            IReadOnlyList<DuckShopOffer> completeBundle,
            IReadOnlyList<string> beforeDreamInventory)
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
            var ownedTypeCount = beforeDreamInventory.Count(definitionId =>
                observation.Rules.Encounter(definitionId).EncounterType == definition.EncounterType);
            var diversityAdjustment = ownedTypeCount == 0 ? 0.7 : -0.25 * ownedTypeCount;
            var synergy = definition.EncounterType == DuckEncounterType.Splash
                && completeBundle.Any(item => item.ShopType == DuckEncounterType.Signpost)
                ? 0.25
                : 0;
            var currencyCostWeight = observation.Economy.UsesStars ? 0.72 : 0.18;
            return movementValue + twigValue + abilityValue + diversityAdjustment + synergy - offer.Price * currencyCostWeight;
        }

        private static string BundleKey(IEnumerable<DuckShopOffer> bundle) =>
            string.Join("|", bundle.Select(offer => offer.DefinitionId).OrderBy(id => id, StringComparer.Ordinal));

        private sealed class AdventureSearch
        {
            private static readonly int[] NoKnownChips = Array.Empty<int>();
            private readonly DuckMatchView _observation;
            private readonly DuckPlayerView _player;
            private readonly int _maximumNodes;

            internal AdventureSearch(DuckMatchView observation, DuckPlayerView player, int maximumNodes)
            {
                _observation = observation;
                _player = player;
                _maximumNodes = maximumNodes;
            }

            internal int Nodes { get; private set; }

            internal double DrawValue(
                PlannedAdventureState state,
                int[] counts,
                int[] known,
                int depth)
            {
                CountNode();
                if (depth <= 0) return PlannedRestValue(_observation, _player, state);

                var remainingCount = counts.Sum();
                if (remainingCount == 0) return PlannedRestValue(_observation, _player, state);
                if (known.Length > 0)
                {
                    var index = known[0];
                    if (counts[index] <= 0)
                        throw new InvalidOperationException("A Signpost preview is absent from the observed remaining pouch.");
                    return CandidateValue(state, counts, known, depth, index);
                }

                var total = 0.0;
                for (var index = 0; index < counts.Length; index++)
                {
                    if (counts[index] == 0) continue;
                    total += counts[index] / (double)remainingCount
                        * CandidateValue(state, counts, NoKnownChips, depth, index);
                }
                return total;
            }

            private double CandidateValue(
                PlannedAdventureState state,
                int[] counts,
                int[] known,
                int depth,
                int definitionIndex)
            {
                CountNode();
                var remaining = (int[])counts.Clone();
                remaining[definitionIndex]--;
                var definition = _observation.Rules.EncounterDefinitions[definitionIndex];
                var placement = DuckAdventureRules.ApplyEncounter(
                    state.State,
                    definition,
                    _observation.CurrentEvent.EventType);
                var after = new PlannedAdventureState(
                    placement.State,
                    ScoringFinalType(placement.EncounterType),
                    ScoringFinalType(placement.EncounterType).HasValue && placement.NuisanceSuppressed,
                    state.FutureTwigs + placement.ReedsTwigsAwarded + placement.EventTwigsAwarded,
                    placement.WearsOut);
                var nextKnown = known.Length <= 1 ? NoKnownChips : known.Skip(1).ToArray();
                if (placement.WearsOut || placement.State.Position == 43 || remaining.Sum() == 0 || depth == 1)
                    return PlannedRestValue(_observation, _player, after);

                if (placement.EncounterType == DuckEncounterType.Signpost)
                {
                    var previewCount = _observation.CurrentEvent.EventType switch
                    {
                        DuckWorldEventType.SunlitSignboards => 2,
                        DuckWorldEventType.ThickMorningMist => 0,
                        _ => 1
                    };
                    return RevealedDecisionValue(after, remaining, nextKnown, depth - 1, previewCount);
                }
                return DecisionValue(after, remaining, nextKnown, depth - 1);
            }

            private double DecisionValue(
                PlannedAdventureState state,
                int[] counts,
                int[] known,
                int depth)
            {
                CountNode();
                var settleValue = PlannedRestValue(_observation, _player, state);
                var continueValue = DrawValue(state, counts, known, depth);
                return Math.Max(settleValue, continueValue);
            }

            private double RevealedDecisionValue(
                PlannedAdventureState state,
                int[] counts,
                int[] alreadyKnown,
                int depth,
                int previewCount)
            {
                CountNode();
                if (previewCount == 0)
                    return DecisionValue(state, counts, NoKnownChips, depth);

                var targetCount = Math.Min(previewCount, counts.Sum());
                var prefix = alreadyKnown.Take(targetCount).ToList();
                var unseenCounts = (int[])counts.Clone();
                foreach (var known in prefix)
                {
                    if (unseenCounts[known] <= 0)
                        throw new InvalidOperationException("A Signpost preview is absent from the observed remaining pouch.");
                    unseenCounts[known]--;
                }
                return RevealAdditional();

                double RevealAdditional()
                {
                    CountNode();
                    if (prefix.Count == targetCount)
                        return DecisionValue(state, counts, prefix.ToArray(), depth);

                    var unseenTotal = unseenCounts.Sum();
                    if (unseenTotal == 0)
                        return DecisionValue(state, counts, prefix.ToArray(), depth);
                    var expected = 0.0;
                    for (var index = 0; index < unseenCounts.Length; index++)
                    {
                        if (unseenCounts[index] == 0) continue;
                        var weight = unseenCounts[index] / (double)unseenTotal;
                        unseenCounts[index]--;
                        prefix.Add(index);
                        expected += weight * RevealAdditional();
                        prefix.RemoveAt(prefix.Count - 1);
                        unseenCounts[index]++;
                    }
                    return expected;
                }
            }

            private void CountNode()
            {
                if (Nodes >= _maximumNodes) throw new PlanningLimitExceededException();
                Nodes++;
            }
        }

        private readonly struct PlannedAdventureState
        {
            internal PlannedAdventureState(
                DuckAdventureState state,
                DuckEncounterType? finalType,
                bool finalSuppressed,
                int futureTwigs,
                bool wornOut)
            {
                State = state;
                FinalType = finalType;
                FinalSuppressed = finalSuppressed;
                FutureTwigs = futureTwigs;
                WornOut = wornOut;
            }

            internal DuckAdventureState State { get; }
            internal DuckEncounterType? FinalType { get; }
            internal bool FinalSuppressed { get; }
            internal int FutureTwigs { get; }
            internal bool WornOut { get; }
        }

        private sealed class AdventurePlan
        {
            internal AdventurePlan(
                double currentValue,
                double exploreValue,
                double immediateWearChance,
                int completedDepth,
                int completedNodes,
                int totalNodes,
                bool currentRestProvablyLoses,
                bool optimisticRecoveryPossible,
                bool knownFirstWearsOut,
                string knownFirstName,
                int knownFirstSafeMaximum)
            {
                CurrentValue = currentValue;
                ExploreValue = exploreValue;
                ImmediateWearChance = immediateWearChance;
                CompletedDepth = completedDepth;
                CompletedNodes = completedNodes;
                TotalNodes = totalNodes;
                CurrentRestProvablyLoses = currentRestProvablyLoses;
                OptimisticRecoveryPossible = optimisticRecoveryPossible;
                KnownFirstWearsOut = knownFirstWearsOut;
                KnownFirstName = knownFirstName;
                KnownFirstSafeMaximum = knownFirstSafeMaximum;
            }

            internal double CurrentValue { get; }
            internal double ExploreValue { get; }
            internal double ImmediateWearChance { get; }
            internal int CompletedDepth { get; }
            internal int CompletedNodes { get; }
            internal int TotalNodes { get; }
            internal bool CurrentRestProvablyLoses { get; }
            internal bool OptimisticRecoveryPossible { get; }
            internal bool KnownFirstWearsOut { get; }
            internal string KnownFirstName { get; }
            internal int KnownFirstSafeMaximum { get; }
        }

        private sealed class PlanningLimitExceededException : Exception
        {
        }

        private readonly struct FinalScoreBound
        {
            internal FinalScoreBound(int twigs, int sleep)
            {
                Twigs = twigs;
                Sleep = sleep;
            }

            internal int Twigs { get; }
            internal int Sleep { get; }
        }

        private sealed class PurchaseBundle
        {
            internal PurchaseBundle(
                IReadOnlyList<DuckShopOffer> extension,
                IReadOnlyList<DuckShopOffer> complete,
                double value)
            {
                Extension = extension;
                Complete = complete;
                Value = value;
            }

            internal IReadOnlyList<DuckShopOffer> Extension { get; }
            internal IReadOnlyList<DuckShopOffer> Complete { get; }
            internal double Value { get; }
        }
    }
}
