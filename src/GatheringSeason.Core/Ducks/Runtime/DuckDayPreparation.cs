using System;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Runtime
{
    internal static class DuckDayPreparation
    {
        /// <summary>Advance one completed Dream into the next Day's ordered Dawn preparation.</summary>
        internal static void BeginNextDay(DuckMatchRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var state = runtime.State;
            if (state.Phase != DuckPhase.DayComplete
                || state.Day < 1
                || state.Day >= DuckMatchSettings.StandardDays
                || state.Players.Any(player => !player.HasFinishedDream))
                throw new InvalidOperationException("Only a completed Dream on Days 1–9 can advance the calendar.");

            // Freeze all Twig deficits before any gifts, resets or temporary-step activation.
            var leadingTwigs = state.Players.Max(player => player.TotalTwigs);
            var deficits = state.Players.ToDictionary(player => player.Id, player => leadingTwigs - player.TotalTwigs);
            var nextDay = state.Day + 1;
            state.Day = nextDay;
            state.CurrentEventIndex++;
            state.FinalDayDecisionBeat = nextDay == DuckMatchSettings.StandardDays ? 1 : 0;
            state.FinalDayCommits.Clear();

            if (nextDay == 5 && !state.DayFiveGooseAdded)
            {
                foreach (var player in state.Players)
                    player.Inventory.Add(new DuckPhysicalChipState(state.NextPhysicalChipId++, "grumpy_goose"));
                state.DayFiveGooseAdded = true;
            }

            foreach (var player in state.Players)
            {
                player.DayReedsTwigs = 0;
                player.DayEventTwigs = 0;
                player.Exhaustion = 0;
                player.SafeExhaustionMaximum = 5;
                player.ActiveFlock = 0;
                player.SplashProtectionArmed = false;
                player.LogSlowdownPending = false;
                player.GuideProtectionAvailable = false;
                player.PocketDriftwoodAwarded = false;
                player.HasGloriousSunshineChoice = false;
                player.GloriousSunshineBenefit = default;
                player.FlowersPlaced = 0;
                player.FrozenSleep = 0;
                player.IsSleepFrozen = false;
                player.RemainingSleep = 0;
                player.HasFinishedDay = false;
                player.IsWornOut = false;
                player.HasFinishedDream = false;
                player.PlacedChips.Clear();
                player.PlacedHelpfulTypes.Clear();
                player.PurchasedEncounterDefinitionIds.Clear();
                player.PurchasedShopTypes.Clear();
                player.KnownNextPhysicalChipIds.Clear();

                player.DawnTwigDeficit = deficits[player.Id];
                player.DawnFeathersAwarded = DawnFeathersForDeficit(player.DawnTwigDeficit);
                DuckFeatherAwards.Give(state, player, player.DawnFeathersAwarded, "dawn_delivery");
                player.ActiveMostRestedStep = player.PendingMostRestedStep;
                player.PendingMostRestedStep = false;

                player.BagPhysicalChipIds.Clear();
                player.BagPhysicalChipIds.AddRange(player.Inventory.Select(chip => chip.PhysicalChipId));
                runtime.ShuffleInPlace(player.BagPhysicalChipIds);
                player.GuideProtectionAvailable = runtime.CurrentEvent.EventType == DuckWorldEventType.FriendlyGuide;
                // Permanent trail and the one-Day reward stay separate; neither is capped.
                player.EffectiveStart = player.PermanentFeatherTrail + (player.ActiveMostRestedStep ? 1 : 0);
                player.Position = player.EffectiveStart;
                runtime.AddHistory(player.Id,
                    $"{player.Name} starts Day {nextDay} at {player.EffectiveStart}: trail {player.PermanentFeatherTrail}, temporary Most Rested {(player.ActiveMostRestedStep ? 1 : 0)}; Dawn deficit {player.DawnTwigDeficit} gave {player.DawnFeathersAwarded} {(player.DawnFeathersAwarded == 1 ? "Feather" : "Feathers")}.");
            }
            runtime.AddHistory(string.Empty, "World Event: " + runtime.CurrentEvent.Name + ".");
            state.Phase = DuckPhase.Adventure;
        }

        internal static int DawnFeathersForDeficit(int deficit)
        {
            if (deficit < 0) throw new ArgumentOutOfRangeException(nameof(deficit));
            if (deficit <= 2) return 0;
            if (deficit <= 6) return 1;
            if (deficit <= 10) return 2;
            return 3;
        }
    }
}
