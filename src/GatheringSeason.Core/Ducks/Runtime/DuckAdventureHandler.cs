using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Authoritative Explore/Settle flow and encounter timing for the Duck profile.</summary>
    internal static class DuckAdventureHandler
    {
        private const string ExploreActionId = "duck.explore";
        private const string SettleActionId = "duck.settle";
        private const string FreshAirActionId = "duck.event.glorious-sunshine.fresh-air";
        private const string WarmDreamsActionId = "duck.event.glorious-sunshine.warm-dreams";

        internal static IReadOnlyList<GameAction> GetLegalActions(DuckMatchRuntime runtime, DuckPlayerState player)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (runtime.State.Phase != DuckPhase.Adventure || player.HasFinishedDay || player.IsWornOut)
                return EmptyActions();
            if (runtime.CurrentEvent.EventType == DuckWorldEventType.GloriousSunshine)
            {
                if (!player.HasGloriousSunshineChoice)
                    return new ReadOnlyCollection<GameAction>(new[]
                    {
                        new GameAction(FreshAirActionId, GameActionKind.ChooseEventBenefit,
                            $"Fresh Air (+{runtime.Rules.FreshAirExhaustionBonus} safe Exhaustion today)"),
                        new GameAction(WarmDreamsActionId, GameActionKind.ChooseEventBenefit,
                            $"Starry Night (+{runtime.Rules.Economy.WarmDreamsReward} {runtime.Rules.Economy.CurrencyName} tonight)")
                    });
                if (runtime.State.Players.Any(candidate => !candidate.HasGloriousSunshineChoice))
                    return EmptyActions();
            }
            if (player.PendingTailwindPhysicalChipId != 0)
            {
                if (player.PendingTailwindDestination != 0) return EmptyActions();
                var pending = PendingTailwind(runtime, player)!;
                var choices = new List<GameAction>
                {
                    new GameAction("duck.tailwind.full", GameActionKind.ChooseTailwind, "Move " + pending.FullMovement + " spaces")
                };
                if (pending.ShelterPosition.HasValue)
                    choices.Add(new GameAction("duck.tailwind.shelter", GameActionKind.ChooseTailwind,
                        "Settle at shelter " + pending.ShelterPosition.Value));
                return new ReadOnlyCollection<GameAction>(choices);
            }
            if (runtime.State.Day == DuckMatchSettings.StandardDays && runtime.State.FinalDayCommits.Any(
                commit => commit.Beat == runtime.State.FinalDayDecisionBeat && commit.PlayerId == player.Id))
                return EmptyActions();

            var actions = new List<GameAction>();
            if (player.BagPhysicalChipIds.Count > 0)
                actions.Add(new GameAction(ExploreActionId, GameActionKind.Explore, "Explore"));
            if (player.PlacedChips.Count > 0)
                actions.Add(new GameAction(SettleActionId, GameActionKind.Settle, "Settle down"));
            return new ReadOnlyCollection<GameAction>(actions);
        }

        internal static void Execute(DuckMatchRuntime runtime, DuckPlayerState player, GameAction action)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (action == null) throw new ArgumentNullException(nameof(action));

            var legal = GetLegalActions(runtime, player).SingleOrDefault(candidate =>
                string.Equals(candidate.Id, action.Id, StringComparison.Ordinal));
            if (legal == null)
                throw new InvalidOperationException("Action '" + action.Id + "' is not legal for " + player.Id + " in Duck Adventure.");

            if (legal.Kind == GameActionKind.ChooseEventBenefit)
            {
                ChooseGloriousSunshineBenefit(runtime, player, legal.Id);
                return;
            }

            if (legal.Kind == GameActionKind.ChooseTailwind)
            {
                var pending = PendingTailwind(runtime, player)!;
                player.PendingTailwindSettle = legal.Id == "duck.tailwind.shelter";
                player.PendingTailwindDestination = legal.Id == "duck.tailwind.shelter" ? pending.ShelterPosition!.Value : pending.FullPosition;
                if (runtime.State.Day == DuckMatchSettings.StandardDays) CompleteFinalDayCohort(runtime);
                else
                {
                    Explore(runtime, player, legal.Id == "duck.tailwind.shelter");
                    ResolveNightWhenEveryoneFinished(runtime);
                }
                return;
            }

            if (runtime.State.Day == DuckMatchSettings.StandardDays)
            {
                CommitFinalDayAction(runtime, player, legal.Kind);
                return;
            }

            ResolveAction(runtime, player, legal.Kind);
            ResolveNightWhenEveryoneFinished(runtime);
        }

        private static void ChooseGloriousSunshineBenefit(
            DuckMatchRuntime runtime,
            DuckPlayerState player,
            string actionId)
        {
            var benefit = actionId == FreshAirActionId
                ? DuckGloriousSunshineBenefit.FreshAir
                : actionId == WarmDreamsActionId
                    ? DuckGloriousSunshineBenefit.WarmDreams
                    : throw new InvalidOperationException("Unknown Glorious Sunshine benefit action.");
            player.HasGloriousSunshineChoice = true;
            player.GloriousSunshineBenefit = benefit;
            if (benefit == DuckGloriousSunshineBenefit.FreshAir)
                player.SafeExhaustionMaximum += runtime.Rules.FreshAirExhaustionBonus;
            runtime.AddHistory(player.Id, player.Name + " chose " +
                (benefit == DuckGloriousSunshineBenefit.FreshAir ? "Fresh Air" : "Starry Night") +
                " for Glorious Sunshine.");
        }

        private static void CommitFinalDayAction(DuckMatchRuntime runtime, DuckPlayerState player, GameActionKind actionKind)
        {
            var beat = runtime.State.FinalDayDecisionBeat;
            if (beat < 1) throw new InvalidOperationException("Final-Day Adventure requires a positive decision beat.");
            runtime.State.FinalDayCommits.Add(new DuckFinalDayCommitState(beat, player.Id, actionKind));

            var activeCohort = runtime.State.Players.Where(candidate => !candidate.HasFinishedDay).ToArray();
            if (activeCohort.Any(candidate => !runtime.State.FinalDayCommits.Any(
                commit => commit.Beat == beat && commit.PlayerId == candidate.Id)))
                return;

            if (runtime.Rules.IsWishSetTwo)
                foreach (var candidate in activeCohort)
                    if (runtime.State.FinalDayCommits.Single(commit => commit.PlayerId == candidate.Id).ActionKind == GameActionKind.Explore)
                        ReserveTailwind(runtime, candidate);
            CompleteFinalDayCohort(runtime);
        }

        private static void CompleteFinalDayCohort(DuckMatchRuntime runtime)
        {
            if (runtime.State.Players.Any(player =>
                player.PendingTailwindPhysicalChipId != 0 && player.PendingTailwindDestination == 0)) return;
            var beat = runtime.State.FinalDayDecisionBeat;
            var commits = runtime.State.FinalDayCommits.ToDictionary(commit => commit.PlayerId, StringComparer.Ordinal);
            foreach (var candidate in runtime.State.Players.Where(player => !player.HasFinishedDay))
            {
                var commit = commits[candidate.Id];
                if (commit.ActionKind == GameActionKind.Explore)
                {
                    var pending = PendingTailwind(runtime, candidate);
                    Explore(runtime, candidate, pending != null && candidate.PendingTailwindSettle, allowReserve: false);
                }
                else ResolveAction(runtime, candidate, commit.ActionKind);
            }
            runtime.State.FinalDayCommits.Clear();
            runtime.State.FinalDayDecisionBeat = checked(beat + 1);
            ResolveNightWhenEveryoneFinished(runtime);
        }

        private static bool ReserveTailwind(DuckMatchRuntime runtime, DuckPlayerState player)
        {
            var id = player.BagPhysicalChipIds[0];
            var chip = player.Inventory.Single(candidate => candidate.PhysicalChipId == id);
            if (!runtime.Rules.IsWishSetTwo
                || runtime.Rules.Encounter(chip.DefinitionId).EncounterType != DuckEncounterType.Tailwind) return false;
            player.PendingTailwindPhysicalChipId = id;
            return true;
        }

        internal static DuckAdventureState AdventureState(DuckMatchRuntime runtime, DuckPlayerState player) => new DuckAdventureState(
            player.Position, player.Exhaustion, player.SafeExhaustionMaximum, player.ActiveFlock,
            player.SplashProtectionArmed, player.LogSlowdownPending, player.GuideProtectionAvailable,
            player.PocketDriftwoodAwarded, player.FlowersPlaced, DuckAdventureRules.HelpfulTypes(player.PlacedHelpfulTypes),
            player.PlacedChips.Count > 0 && runtime.Rules.Encounter(player.Inventory.Single(chip => chip.PhysicalChipId == player.PlacedChips.Last().PhysicalChipId).DefinitionId).IsObstacle);

        internal static DuckPendingTailwindView? PendingTailwind(DuckMatchRuntime runtime, DuckPlayerState player)
        {
            if (player.PendingTailwindPhysicalChipId == 0) return null;
            var chip = player.Inventory.Single(candidate => candidate.PhysicalChipId == player.PendingTailwindPhysicalChipId);
            var placement = DuckAdventureRules.ApplyEncounter(AdventureState(runtime, player), runtime.Rules.Encounter(chip.DefinitionId), runtime.CurrentEvent.EventType, runtime.Rules);
            var shelter = DuckAdventureRules.FirstReachableShelter(player.Position, placement.State.Position, runtime.Rules);
            return new DuckPendingTailwindView(chip.DefinitionId, placement.Movement, placement.State.Position, shelter);
        }

        private static void ResolveAction(DuckMatchRuntime runtime, DuckPlayerState player, GameActionKind actionKind)
        {
            switch (actionKind)
            {
                case GameActionKind.Explore:
                    Explore(runtime, player);
                    return;
                case GameActionKind.Settle:
                    FinishAdventure(runtime, player, wornOut: false, "settled down");
                    return;
                default:
                    throw new InvalidOperationException("Unsupported Duck Adventure action: " + actionKind);
            }
        }

        private static void Explore(DuckMatchRuntime runtime, DuckPlayerState player, bool settleAtShelter = false, bool allowReserve = true)
        {
            if (player.BagPhysicalChipIds.Count == 0)
                throw new InvalidOperationException("There is no chip left to explore.");
            if (player.KnownNextPhysicalChipIds.Count > 0
                && player.KnownNextPhysicalChipIds[0] != player.BagPhysicalChipIds[0])
                throw new InvalidOperationException("The saved Signpost preview does not match the next pouch chip.");

            var physicalChipId = player.BagPhysicalChipIds[0];
            var physicalChip = player.Inventory.Single(chip => chip.PhysicalChipId == physicalChipId);
            var definition = runtime.Rules.Encounter(physicalChip.DefinitionId);
            if (allowReserve && player.PendingTailwindPhysicalChipId == 0 && ReserveTailwind(runtime, player)) return;
            var before = AdventureState(runtime, player);
            var placement = DuckAdventureRules.ApplyEncounter(before, definition, runtime.CurrentEvent.EventType, runtime.Rules, settleAtShelter);
            var after = placement.State;
            player.BagPhysicalChipIds.RemoveAt(0);
            if (player.KnownNextPhysicalChipIds.Count > 0)
                player.KnownNextPhysicalChipIds.RemoveAt(0);
            if (player.UnorderedNextPhysicalChipIds.Count > 0)
            {
                player.UnorderedNextPhysicalChipIds.Remove(physicalChipId);
                player.KnownNextPhysicalChipIds.AddRange(player.UnorderedNextPhysicalChipIds);
                player.UnorderedNextPhysicalChipIds.Clear();
            }

            player.Position = after.Position;
            player.PendingTailwindPhysicalChipId = 0;
            player.PendingTailwindDestination = 0;
            player.PendingTailwindSettle = false;
            player.Exhaustion = after.Exhaustion;
            player.SafeExhaustionMaximum = after.SafeExhaustionMaximum;
            player.ActiveFlock = after.ActiveFlock;
            player.SplashProtectionArmed = after.SplashProtectionArmed;
            player.LogSlowdownPending = after.LogSlowdownPending;
            player.GuideProtectionAvailable = after.GuideProtectionAvailable;
            player.PocketDriftwoodAwarded = after.PocketDriftwoodAwarded;
            player.FlowersPlaced = after.FlowersPlaced;
            player.PlacedChips.Add(new DuckPlacedChipState(physicalChipId, player.Position, placement.NuisanceSuppressed));

            if (definition.IsHelpful)
                player.PlacedHelpfulTypes.Add(placement.EncounterType);
            player.DayReedsTwigs += placement.ReedsTwigsAwarded;
            player.DayEventTwigs += placement.EventTwigsAwarded;
            player.TotalTwigs += placement.ReedsTwigsAwarded + placement.EventTwigsAwarded;
            if (placement.EncounterType == DuckEncounterType.Signpost)
                RefreshSignpostPreview(runtime, player);
            if (placement.EventTwigsAwarded > 0)
                runtime.AddHistory(player.Id, player.Name + " gained 1 Twig from " + runtime.CurrentEvent.Name + ".");
            var encounterLabel = definition.IsWish ? "Wish " : "Obstacle ";
            runtime.AddHistory(player.Id, player.Name + " placed " + encounterLabel + definition.Name + " at space " + player.Position + ".");

            if (settleAtShelter)
            {
                FinishAdventure(runtime, player, wornOut: placement.WearsOut, "settled at a shelter");
                return;
            }
            if (placement.WearsOut)
            {
                FinishAdventure(runtime, player, wornOut: true, "became worn out");
                return;
            }
            if (player.Position == 43)
            {
                FinishAdventure(runtime, player, wornOut: false, "reached the oasis");
                return;
            }
            if (player.BagPhysicalChipIds.Count == 0)
                FinishAdventure(runtime, player, wornOut: false, "emptied the pouch");
        }

        private static void RefreshSignpostPreview(DuckMatchRuntime runtime, DuckPlayerState player)
        {
            var count = runtime.CurrentEvent.EventType == DuckWorldEventType.ThickMorningMist ? 0 : runtime.Rules.IsWishSetTwo ? 2 : 1;
            if (count == 0)
            {
                player.KnownNextPhysicalChipIds.Clear();
                player.UnorderedNextPhysicalChipIds.Clear();
            }
            else if (!runtime.Rules.IsWishSetTwo || player.KnownNextPhysicalChipIds.Count > 0 || player.BagPhysicalChipIds.Count <= 1)
            {
                player.KnownNextPhysicalChipIds.Clear();
                player.KnownNextPhysicalChipIds.AddRange(player.BagPhysicalChipIds.Take(count));
                player.UnorderedNextPhysicalChipIds.Clear();
            }
            else
            {
                player.UnorderedNextPhysicalChipIds.Clear();
                player.UnorderedNextPhysicalChipIds.AddRange(player.BagPhysicalChipIds.Take(count).OrderBy(id => id));
            }
        }

        private static void FinishAdventure(
            DuckMatchRuntime runtime,
            DuckPlayerState player,
            bool wornOut,
            string reason)
        {
            player.HasFinishedDay = true;
            player.IsWornOut = wornOut;
            player.KnownNextPhysicalChipIds.Clear();
            player.UnorderedNextPhysicalChipIds.Clear();
            player.SplashProtectionArmed = false;
            player.LogSlowdownPending = false;
            player.GuideProtectionAvailable = false;
            runtime.AddHistory(player.Id, player.Name + " " + reason + " at space " + player.Position + ".");
        }

        private static void ResolveNightWhenEveryoneFinished(DuckMatchRuntime runtime)
        {
            if (runtime.State.Phase == DuckPhase.Adventure && runtime.State.Players.All(player => player.HasFinishedDay))
                DuckNightResolver.Resolve(runtime.State, runtime.Rules);
        }

        private static IReadOnlyList<GameAction> EmptyActions()
        {
            return new ReadOnlyCollection<GameAction>(Array.Empty<GameAction>());
        }
    }
}
