using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using GatheringSeason.Core.Randomness;

namespace GatheringSeason.Core.Ducks.Persistence
{
    /// <summary>Captures and restores the complete authoritative state of Duck rules v1.</summary>
    public static class DuckSaves
    {
        public const int CurrentFormatVersion = 1;
        public const int CurrentRulesVersion = DuckRules.CurrentRulesRevision;

        public static DuckSaveData Capture(MatchSession<DuckMatchView> session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var runtime = session.DuckRuntime();
            var state = runtime.State;
            var revisions = session.CaptureCommandRevisions(state.Players.Select(player => player.Id));
            var save = new DuckSaveData
            {
                FormatVersion = CurrentFormatVersion,
                ProfileId = state.ProfileId,
                RulesVersion = state.RulesRevision,
                Settings = new DuckSaveSettingsData
                {
                    Days = state.Settings.Days,
                    StartingFeathers = state.Settings.StartingFeathers
                },
                Day = state.Day,
                Phase = state.Phase,
                CurrentEventIndex = state.CurrentEventIndex,
                DayFiveGooseAdded = state.DayFiveGooseAdded,
                FinalDayDecisionBeat = state.FinalDayDecisionBeat,
                NextPhysicalChipId = state.NextPhysicalChipId,
                Random = new DuckRandomSaveData
                {
                    Algorithm = state.RandomState.Algorithm,
                    State = state.RandomState.State,
                    Increment = state.RandomState.Increment
                },
                WorldEventDeckDefinitionIds = state.WorldEventDeckDefinitionIds.ToList(),
                Players = state.Players.Select(CapturePlayer).ToList(),
                History = state.History.Select(entry => new DuckHistorySaveData
                {
                    Day = entry.Day,
                    ActorId = entry.ActorId,
                    Message = entry.Message
                }).ToList(),
                PublicAwards = state.PublicAwards.Select(award => new DuckPublicAwardSaveData
                {
                    Day = award.Day,
                    DefinitionId = award.DefinitionId,
                    PlayerIds = award.PlayerIds.ToList(),
                    Sleep = award.Sleep,
                    Twigs = award.Twigs,
                    Feathers = award.Feathers
                }).ToList(),
                FinalDayCommits = state.FinalDayCommits.Select(commit => new DuckFinalDayCommitSaveData
                {
                    Beat = commit.Beat,
                    PlayerId = commit.PlayerId,
                    ActionKind = commit.ActionKind
                }).ToList(),
                CommandRevisions = revisions.Select(revision => new DuckCommandRevisionSaveData
                {
                    PlayerId = revision.Key,
                    Revision = revision.Value
                }).ToList(),
                FinalResult = CaptureFinalResult(state.FinalResult)
            };
            Validate(save);
            return save;
        }

        public static MatchSession<DuckMatchView> Restore(DuckSaveData save)
        {
            Validate(save);
            var state = new DuckMatchState(DuckMatchSettings.Standard, save.RulesVersion)
            {
                Day = save.Day,
                Phase = save.Phase,
                CurrentEventIndex = save.CurrentEventIndex,
                DayFiveGooseAdded = save.DayFiveGooseAdded,
                FinalDayDecisionBeat = save.FinalDayDecisionBeat,
                NextPhysicalChipId = save.NextPhysicalChipId,
                RandomState = new RandomState(save.Random.Algorithm, save.Random.State, save.Random.Increment),
                FinalResult = RestoreFinalResult(save.FinalResult)
            };
            state.WorldEventDeckDefinitionIds.AddRange(save.WorldEventDeckDefinitionIds);
            state.Players.AddRange(save.Players.Select(RestorePlayer));
            state.History.AddRange(save.History.Select(entry =>
                new DuckHistoryState(entry.Day, entry.ActorId, entry.Message)));
            state.PublicAwards.AddRange(save.PublicAwards.Select(award =>
                new DuckPublicAwardState(award.Day, award.DefinitionId, award.PlayerIds,
                    award.Sleep, award.Twigs, award.Feathers)));
            state.FinalDayCommits.AddRange(save.FinalDayCommits.Select(commit =>
                new DuckFinalDayCommitState(commit.Beat, commit.PlayerId, commit.ActionKind)));
            var revisions = save.CommandRevisions.ToDictionary(
                revision => revision.PlayerId, revision => revision.Revision, StringComparer.Ordinal);
            return MatchSession<DuckMatchView>.RestoreDuck(DuckMatchRuntime.Restore(state), revisions);
        }

        /// <summary>Rejects missing, unsupported, malformed or internally inconsistent v1 data.</summary>
        public static void Validate(DuckSaveData save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            Require(save.FormatVersion == CurrentFormatVersion,
                "FormatVersion must identify Duck save format version 1.");
            Require(string.Equals(save.ProfileId, DuckRules.V1.ProfileId, StringComparison.Ordinal),
                "ProfileId must identify the Duck v1 rules profile.");
            Require(save.RulesVersion >= 1 && save.RulesVersion <= CurrentRulesVersion,
                "RulesVersion must identify supported Duck rules revision 1, 2, 3, 4, 5, 6 or 7.");
            var rules = DuckRules.ForRulesRevision(save.RulesVersion);
            Require(save.Settings != null, "Settings are required.");
            Require(save.Settings.Days == DuckMatchSettings.StandardDays,
                "Settings.Days must match the ten-Day rules profile.");
            Require(save.Settings.StartingFeathers == 0,
                "Settings.StartingFeathers must match the approved fixed-zero setting.");
            Require(save.Day >= 1 && save.Day <= DuckMatchSettings.StandardDays, "Day is outside the supported calendar.");
            Require(Enum.IsDefined(typeof(DuckPhase), save.Phase), "Phase is not defined.");
            Require(save.Phase != DuckPhase.Preparation, "Preparation is not a resumable command boundary.");
            Require(save.CurrentEventIndex == save.Day - 1, "CurrentEventIndex must identify the current Day's event.");
            Require(save.Random != null, "Random continuation state is required.");
            Require(save.Random.Algorithm == RandomState.CurrentAlgorithm,
                "Random.Algorithm is missing or unsupported.");
            Require((save.Random.Increment & 1UL) == 1UL, "Random.Increment must be an odd PCG stream value.");

            Require(save.WorldEventDeckDefinitionIds != null, "WorldEventDeckDefinitionIds are required.");
            var eventIds = rules.WorldEvents.Select(item => item.DefinitionId).ToHashSet(StringComparer.Ordinal);
            Require(save.WorldEventDeckDefinitionIds.Count == eventIds.Count
                    && save.WorldEventDeckDefinitionIds.Distinct(StringComparer.Ordinal).Count() == eventIds.Count
                    && save.WorldEventDeckDefinitionIds.All(eventIds.Contains),
                "WorldEventDeckDefinitionIds must contain each v1 event exactly once.");

            Require(save.Players != null, "Players are required.");
            Require(save.Players.Count == 2, "A Duck v1 save needs exactly two players.");
            var expectedPlayerIds = new HashSet<string>(new[] { "human", "ai" }, StringComparer.Ordinal);
            Require(save.Players[0] != null && save.Players[0].Id == "human"
                    && save.Players[1] != null && save.Players[1].Id == "ai",
                "Players must contain the human then AI in fixed reveal order.");

            var physicalIds = new HashSet<int>();
            foreach (var player in save.Players)
                ValidatePlayer(save, player, physicalIds, rules);
            var currentEventType = rules.WorldEvent(save.WorldEventDeckDefinitionIds[save.CurrentEventIndex]).EventType;
            if (currentEventType == DuckWorldEventType.GloriousSunshine
                && (save.Phase != DuckPhase.Adventure
                    || save.Players.Any(player => player.PlacedChips.Count > 0)
                    || save.FinalDayCommits.Count > 0))
                Require(save.Players.All(player => player.HasGloriousSunshineChoice),
                    "Every duck must choose a Glorious Sunshine benefit before Adventure begins.");
            Require(save.NextPhysicalChipId > 0 && physicalIds.All(id => id < save.NextPhysicalChipId),
                "NextPhysicalChipId must be greater than every existing physical chip ID.");

            ValidateHistory(save, expectedPlayerIds);
            ValidateAwards(save, expectedPlayerIds);
            ValidateCommits(save, expectedPlayerIds);
            ValidateRevisions(save, expectedPlayerIds);
            ValidateGoose(save);
            ValidateFinalResult(save, expectedPlayerIds);
            ValidatePhase(save);
        }

        private static DuckPlayerSaveData CapturePlayer(DuckPlayerState player)
        {
            return new DuckPlayerSaveData
            {
                Id = player.Id,
                Name = player.Name,
                PermanentFeatherTrail = player.PermanentFeatherTrail,
                TotalTwigs = player.TotalTwigs,
                DayReedsTwigs = player.DayReedsTwigs,
                DayEventTwigs = player.DayEventTwigs,
                Position = player.Position,
                Exhaustion = player.Exhaustion,
                SafeExhaustionMaximum = player.SafeExhaustionMaximum,
                ActiveFlock = player.ActiveFlock,
                SplashProtectionArmed = player.SplashProtectionArmed,
                LogSlowdownPending = player.LogSlowdownPending,
                GuideProtectionAvailable = player.GuideProtectionAvailable,
                PocketDriftwoodAwarded = player.PocketDriftwoodAwarded,
                HasGloriousSunshineChoice = player.HasGloriousSunshineChoice,
                GloriousSunshineBenefit = player.GloriousSunshineBenefit,
                FlowersPlaced = player.FlowersPlaced,
                FrozenSleep = player.FrozenSleep,
                IsSleepFrozen = player.IsSleepFrozen,
                RemainingSleep = player.RemainingSleep,
                PendingMostRestedStep = player.PendingMostRestedStep,
                ActiveMostRestedStep = player.ActiveMostRestedStep,
                EffectiveStart = player.EffectiveStart,
                HasFinishedDay = player.HasFinishedDay,
                HasFinishedDream = player.HasFinishedDream,
                IsWornOut = player.IsWornOut,
                DawnTwigDeficit = player.DawnTwigDeficit,
                DawnFeathersAwarded = player.DawnFeathersAwarded,
                Inventory = player.Inventory.Select(chip => new DuckPhysicalChipSaveData
                {
                    PhysicalChipId = chip.PhysicalChipId,
                    DefinitionId = chip.DefinitionId
                }).ToList(),
                BagPhysicalChipIds = player.BagPhysicalChipIds.ToList(),
                KnownNextPhysicalChipIds = player.KnownNextPhysicalChipIds.ToList(),
                PlacedChips = player.PlacedChips.Select(chip => new DuckPlacedChipSaveData
                {
                    PhysicalChipId = chip.PhysicalChipId,
                    Position = chip.Position,
                    NuisanceSuppressed = chip.NuisanceSuppressed
                }).ToList(),
                PlacedHelpfulTypes = player.PlacedHelpfulTypes.OrderBy(type => type).ToList(),
                PurchasedEncounterDefinitionIds = player.PurchasedEncounterDefinitionIds.ToList(),
                PurchasedShopTypes = player.PurchasedShopTypes.OrderBy(type => type).ToList(),
                LastNightOutcome = CaptureOutcome(player.LastNightOutcome)
            };
        }

        private static DuckPlayerState RestorePlayer(DuckPlayerSaveData saved)
        {
            var player = new DuckPlayerState(saved.Id, saved.Name, 0)
            {
                PermanentFeatherTrail = saved.PermanentFeatherTrail,
                TotalTwigs = saved.TotalTwigs,
                DayReedsTwigs = saved.DayReedsTwigs,
                DayEventTwigs = saved.DayEventTwigs,
                Position = saved.Position,
                Exhaustion = saved.Exhaustion,
                SafeExhaustionMaximum = saved.SafeExhaustionMaximum,
                ActiveFlock = saved.ActiveFlock,
                SplashProtectionArmed = saved.SplashProtectionArmed,
                LogSlowdownPending = saved.LogSlowdownPending,
                GuideProtectionAvailable = saved.GuideProtectionAvailable,
                PocketDriftwoodAwarded = saved.PocketDriftwoodAwarded,
                HasGloriousSunshineChoice = saved.HasGloriousSunshineChoice,
                GloriousSunshineBenefit = saved.GloriousSunshineBenefit,
                FlowersPlaced = saved.FlowersPlaced,
                FrozenSleep = saved.FrozenSleep,
                IsSleepFrozen = saved.IsSleepFrozen,
                RemainingSleep = saved.RemainingSleep,
                PendingMostRestedStep = saved.PendingMostRestedStep,
                ActiveMostRestedStep = saved.ActiveMostRestedStep,
                EffectiveStart = saved.EffectiveStart,
                HasFinishedDay = saved.HasFinishedDay,
                HasFinishedDream = saved.HasFinishedDream,
                IsWornOut = saved.IsWornOut,
                DawnTwigDeficit = saved.DawnTwigDeficit,
                DawnFeathersAwarded = saved.DawnFeathersAwarded,
                LastNightOutcome = RestoreOutcome(saved.LastNightOutcome)
            };
            player.Inventory.AddRange(saved.Inventory.Select(chip =>
                new DuckPhysicalChipState(chip.PhysicalChipId, chip.DefinitionId)));
            player.BagPhysicalChipIds.AddRange(saved.BagPhysicalChipIds);
            player.KnownNextPhysicalChipIds.AddRange(saved.KnownNextPhysicalChipIds);
            player.PlacedChips.AddRange(saved.PlacedChips.Select(chip =>
                new DuckPlacedChipState(chip.PhysicalChipId, chip.Position, chip.NuisanceSuppressed)));
            player.PlacedHelpfulTypes.UnionWith(saved.PlacedHelpfulTypes);
            player.PurchasedEncounterDefinitionIds.AddRange(saved.PurchasedEncounterDefinitionIds);
            player.PurchasedShopTypes.UnionWith(saved.PurchasedShopTypes);
            return player;
        }

        private static DuckNightOutcomeSaveData? CaptureOutcome(DuckNightOutcome? outcome)
        {
            if (outcome == null) return null;
            return new DuckNightOutcomeSaveData
            {
                Day = outcome.Day,
                PrintedSleep = outcome.PrintedSleep,
                PrintedTwigs = outcome.PrintedTwigs,
                ReedsTwigs = outcome.ReedsTwigs,
                EventTwigs = outcome.EventTwigs,
                BramblesPenalty = outcome.BramblesPenalty,
                FlowerSleep = outcome.FlowerSleep,
                FinalHavenSleep = outcome.FinalShelterSleep,
                RestlessNightPenalty = outcome.RestlessNightPenalty,
                CollectiveEventSleep = outcome.CollectiveEventSleep,
                GloriousSunshineSleep = outcome.GloriousSunshineSleep,
                FlockSleep = outcome.FlockSleep,
                PebblesPenalty = outcome.PebblesPenalty,
                SleepBeforeWear = outcome.SleepBeforeWear,
                FrozenSleep = outcome.FrozenSleep,
                TotalTwigsEarned = outcome.TotalTwigsEarned,
                FeathersAwarded = outcome.FeathersAwarded,
                IsMostRested = outcome.IsMostRested,
                NextDayTemporaryStep = outcome.NextDayTemporaryStep,
                DreamTwigs = outcome.DreamTwigs
            };
        }

        private static DuckNightOutcome? RestoreOutcome(DuckNightOutcomeSaveData? outcome)
        {
            if (outcome == null) return null;
            return new DuckNightOutcome(
                outcome.Day, outcome.PrintedSleep, outcome.PrintedTwigs, outcome.ReedsTwigs,
                outcome.EventTwigs, outcome.BramblesPenalty, outcome.FlowerSleep, outcome.FinalHavenSleep,
                outcome.RestlessNightPenalty, outcome.CollectiveEventSleep, outcome.GloriousSunshineSleep, outcome.FlockSleep,
                outcome.PebblesPenalty, outcome.SleepBeforeWear, outcome.FrozenSleep,
                outcome.TotalTwigsEarned, outcome.FeathersAwarded, outcome.IsMostRested,
                outcome.NextDayTemporaryStep, outcome.DreamTwigs);
        }

        private static DuckFinalResultSaveData? CaptureFinalResult(DuckFinalResult? result)
        {
            if (result == null) return null;
            return new DuckFinalResultSaveData
            {
                Standings = result.Standings.Select(standing => new DuckFinalStandingSaveData
                {
                    PlayerId = standing.PlayerId,
                    PlayerName = standing.PlayerName,
                    Rank = standing.Rank,
                    TotalTwigs = standing.TotalTwigs,
                    FrozenNightTenSleep = standing.FrozenNightTenSleep,
                    DreamTwigs = standing.DreamTwigs,
                    IsWinner = standing.IsWinner
                }).ToList(),
                WinnerIds = result.WinnerIds.ToList()
            };
        }

        private static DuckFinalResult? RestoreFinalResult(DuckFinalResultSaveData? result)
        {
            if (result == null) return null;
            return new DuckFinalResult(result.Standings.Select(standing => new DuckFinalStanding(
                standing.PlayerId, standing.PlayerName, standing.Rank, standing.TotalTwigs,
                standing.FrozenNightTenSleep, standing.DreamTwigs, standing.IsWinner)));
        }

        private static void ValidatePlayer(
            DuckSaveData save,
            DuckPlayerSaveData player,
            ISet<int> allPhysicalIds,
            DuckRuleDefinitions rules)
        {
            Require(!string.IsNullOrWhiteSpace(player.Name), $"Players[{player.Id}].Name is required.");
            NonNegative(player.PermanentFeatherTrail, $"Players[{player.Id}].PermanentFeatherTrail");
            NonNegative(player.TotalTwigs, $"Players[{player.Id}].TotalTwigs");
            NonNegative(player.DayReedsTwigs, $"Players[{player.Id}].DayReedsTwigs");
            NonNegative(player.DayEventTwigs, $"Players[{player.Id}].DayEventTwigs");
            Require(player.TotalTwigs >= player.DayReedsTwigs + player.DayEventTwigs,
                $"Players[{player.Id}].TotalTwigs cannot be below current-Day Twigs already added to the nest.");
            Require(player.Position >= 0 && player.Position <= 43, $"Players[{player.Id}].Position is outside the board.");
            NonNegative(player.Exhaustion, $"Players[{player.Id}].Exhaustion");
            var choseFreshAir = save.RulesVersion >= 3 && player.HasGloriousSunshineChoice
                && player.GloriousSunshineBenefit == DuckGloriousSunshineBenefit.FreshAir;
            var validSafeMaximum = choseFreshAir
                ? player.SafeExhaustionMaximum == rules.FreshAirGooseSafeExhaustionMaximum
                    || player.SafeExhaustionMaximum == rules.FreshAirSafeExhaustionMaximum
                : player.SafeExhaustionMaximum == 4 || player.SafeExhaustionMaximum == 5;
            Require(validSafeMaximum,
                $"Players[{player.Id}].SafeExhaustionMaximum is not a supported threshold.");
            NonNegative(player.ActiveFlock, $"Players[{player.Id}].ActiveFlock");
            var currentEvent = rules.WorldEvent(save.WorldEventDeckDefinitionIds[save.CurrentEventIndex]).EventType;
            Require(!player.HasGloriousSunshineChoice
                    || save.RulesVersion >= 3
                    && currentEvent == DuckWorldEventType.GloriousSunshine
                    && Enum.IsDefined(typeof(DuckGloriousSunshineBenefit), player.GloriousSunshineBenefit),
                $"Players[{player.Id}] has an invalid Glorious Sunshine choice.");
            if (save.RulesVersion >= 3 && save.Phase == DuckPhase.Adventure
                && currentEvent == DuckWorldEventType.GloriousSunshine && player.PlacedChips.Count > 0)
                Require(player.HasGloriousSunshineChoice,
                    $"Players[{player.Id}] began Adventure without a Glorious Sunshine choice.");
            NonNegative(player.FlowersPlaced, $"Players[{player.Id}].FlowersPlaced");
            NonNegative(player.FrozenSleep, $"Players[{player.Id}].FrozenSleep");
            NonNegative(player.RemainingSleep, $"Players[{player.Id}].RemainingSleep");
            Require(player.RemainingSleep <= player.FrozenSleep,
                $"Players[{player.Id}].RemainingSleep cannot exceed frozen Sleep.");
            NonNegative(player.EffectiveStart, $"Players[{player.Id}].EffectiveStart");
            if (save.Phase == DuckPhase.Adventure)
                Require(player.EffectiveStart == player.PermanentFeatherTrail + (player.ActiveMostRestedStep ? 1 : 0),
                    $"Players[{player.Id}].EffectiveStart does not match trail and temporary movement.");
            Require(!player.IsWornOut || player.HasFinishedDay,
                $"Players[{player.Id}] cannot be worn out before finishing Adventure.");
            NonNegative(player.DawnTwigDeficit, $"Players[{player.Id}].DawnTwigDeficit");
            Require(player.DawnFeathersAwarded >= 0 && player.DawnFeathersAwarded <= 3,
                $"Players[{player.Id}].DawnFeathersAwarded is outside the Dawn table.");
            Require(player.Inventory != null, $"Players[{player.Id}].Inventory is required.");
            Require(player.BagPhysicalChipIds != null, $"Players[{player.Id}].BagPhysicalChipIds are required.");
            Require(player.KnownNextPhysicalChipIds != null, $"Players[{player.Id}].KnownNextPhysicalChipIds are required.");
            Require(player.PlacedChips != null, $"Players[{player.Id}].PlacedChips are required.");
            Require(player.PlacedHelpfulTypes != null, $"Players[{player.Id}].PlacedHelpfulTypes are required.");
            Require(player.PurchasedEncounterDefinitionIds != null,
                $"Players[{player.Id}].PurchasedEncounterDefinitionIds are required.");
            Require(player.PurchasedShopTypes != null, $"Players[{player.Id}].PurchasedShopTypes are required.");

            var inventoryIds = new HashSet<int>();
            var inventoryById = new Dictionary<int, string>();
            foreach (var chip in player.Inventory)
            {
                Require(chip != null, $"Players[{player.Id}].Inventory cannot contain null chips.");
                Require(chip.PhysicalChipId > 0 && inventoryIds.Add(chip.PhysicalChipId)
                        && allPhysicalIds.Add(chip.PhysicalChipId),
                    $"Physical chip ID {chip.PhysicalChipId} is invalid or duplicated.");
                Require(rules.EncounterDefinitions.Any(definition => definition.DefinitionId == chip.DefinitionId),
                    $"Physical chip {chip.PhysicalChipId} has an unknown encounter definition.");
                inventoryById.Add(chip.PhysicalChipId, chip.DefinitionId);
            }
            Require(inventoryIds.Count >= rules.OpeningBag.Count,
                $"Players[{player.Id}].Inventory is smaller than the opening pouch.");
            Require(player.Inventory.Select(chip => chip.PhysicalChipId)
                    .SequenceEqual(player.Inventory.Select(chip => chip.PhysicalChipId).OrderBy(id => id)),
                $"Players[{player.Id}].Inventory must retain physical creation order.");

            var bagIds = new HashSet<int>();
            foreach (var id in player.BagPhysicalChipIds)
                Require(inventoryIds.Contains(id) && bagIds.Add(id),
                    $"Players[{player.Id}] has a missing or duplicate physical chip in pouch order.");
            Require(player.KnownNextPhysicalChipIds.Count <= 2
                    && player.KnownNextPhysicalChipIds.SequenceEqual(
                        player.BagPhysicalChipIds.Take(player.KnownNextPhysicalChipIds.Count)),
                $"Players[{player.Id}] Signpost preview must be an exact prefix of pouch order.");

            var placedIds = new HashSet<int>();
            var previousPosition = player.EffectiveStart;
            foreach (var chip in player.PlacedChips)
            {
                Require(chip != null && inventoryIds.Contains(chip.PhysicalChipId)
                        && !bagIds.Contains(chip.PhysicalChipId) && placedIds.Add(chip.PhysicalChipId),
                    $"Players[{player.Id}] has a missing, duplicated or still-pouched placed chip.");
                Require(chip.Position >= 1 && chip.Position <= 43 && chip.Position >= previousPosition,
                    $"Players[{player.Id}] placed positions must advance in board order.");
                previousPosition = chip.Position;
            }
            if (player.PlacedChips.Count == 0)
                Require(player.Position == player.EffectiveStart,
                    $"Players[{player.Id}] without placements must remain at EffectiveStart.");
            else
                Require(player.Position == player.PlacedChips[player.PlacedChips.Count - 1].Position,
                    $"Players[{player.Id}].Position must equal the last occupied space.");

            var helpfulTypes = player.PlacedChips
                .Select(chip => rules.Encounter(inventoryById[chip.PhysicalChipId]))
                .Where(definition => definition.IsHelpful)
                .Select(definition => definition.EncounterType)
                .ToHashSet();
            Require(player.PlacedHelpfulTypes.All(type => Enum.IsDefined(typeof(DuckEncounterType), type))
                    && player.PlacedHelpfulTypes.Count == player.PlacedHelpfulTypes.Distinct().Count()
                    && helpfulTypes.SetEquals(player.PlacedHelpfulTypes),
                $"Players[{player.Id}].PlacedHelpfulTypes do not match placed chips.");

            Require(player.PurchasedEncounterDefinitionIds.Count <= DuckDreamHandler.PurchaseLimitForDay(save.Day)
                    && player.PurchasedEncounterDefinitionIds.All(id =>
                        rules.ShopOffers.Any(offer => offer.DefinitionId == id)),
                $"Players[{player.Id}] has invalid current-Dream purchases.");
            var expectedPurchasedTypes = player.PurchasedEncounterDefinitionIds
                .Select(id => rules.ShopOffer(id).ShopType).ToHashSet();
            Require(expectedPurchasedTypes.Count == player.PurchasedEncounterDefinitionIds.Count
                    && player.PurchasedShopTypes.Count == player.PurchasedShopTypes.Distinct().Count()
                    && expectedPurchasedTypes.SetEquals(player.PurchasedShopTypes),
                $"Players[{player.Id}].PurchasedShopTypes do not match purchases.");
            if (player.IsSleepFrozen && save.Day < DuckMatchSettings.StandardDays)
            {
                var purchaseTotal = player.PurchasedEncounterDefinitionIds.Sum(id => rules.ShopOffer(id).Price);
                Require(player.HasFinishedDream
                        ? player.RemainingSleep == 0
                        : player.RemainingSleep == player.FrozenSleep - purchaseTotal,
                    $"Players[{player.Id}].RemainingSleep does not match its purchases.");
            }
            var unassignedDefinitions = player.Inventory
                .Where(chip => !bagIds.Contains(chip.PhysicalChipId) && !placedIds.Contains(chip.PhysicalChipId))
                .Select(chip => chip.DefinitionId).OrderBy(id => id).ToArray();
            Require(unassignedDefinitions.SequenceEqual(player.PurchasedEncounterDefinitionIds.OrderBy(id => id)),
                $"Players[{player.Id}] inventory contains chips outside the pouch, trail and current purchases.");

            ValidateOutcome(save, player, rules);
        }

        private static void ValidateOutcome(
            DuckSaveData save,
            DuckPlayerSaveData player,
            DuckRuleDefinitions rules)
        {
            var outcome = player.LastNightOutcome;
            if (outcome == null)
            {
                Require(!player.IsSleepFrozen && player.FrozenSleep == 0 && player.RemainingSleep == 0,
                    $"Players[{player.Id}] has frozen Sleep without a Night outcome.");
                return;
            }
            Require(outcome.Day >= 1 && outcome.Day <= save.Day, $"Players[{player.Id}] has an invalid Night outcome Day.");
            foreach (var value in new[]
            {
                outcome.PrintedSleep, outcome.PrintedTwigs, outcome.ReedsTwigs, outcome.EventTwigs,
                outcome.BramblesPenalty, outcome.FlowerSleep, outcome.FinalHavenSleep,
                outcome.RestlessNightPenalty, outcome.CollectiveEventSleep, outcome.GloriousSunshineSleep, outcome.FlockSleep,
                outcome.PebblesPenalty, outcome.SleepBeforeWear, outcome.FrozenSleep,
                outcome.TotalTwigsEarned, outcome.FeathersAwarded, outcome.DreamTwigs
            }) Require(value >= 0, $"Players[{player.Id}] has a negative Night outcome value.");
            Require(outcome.NextDayTemporaryStep == 0 || outcome.NextDayTemporaryStep == 1,
                $"Players[{player.Id}] has an invalid temporary Night step.");
            var outcomeEvent = rules.WorldEvent(save.WorldEventDeckDefinitionIds[outcome.Day - 1]).EventType;
            var economy = rules.Economy;
            Require(outcome.GloriousSunshineSleep == 0
                    || outcome.GloriousSunshineSleep == economy.WarmDreamsReward
                    && outcomeEvent == DuckWorldEventType.GloriousSunshine,
                $"Players[{player.Id}] has an invalid Glorious Sunshine Sleep award.");
            Require(outcome.FlowerSleep >= 0 && outcome.FlowerSleep <= economy.FlowersRewardLimit
                    && (economy.FlowersPerChipReward == 0
                        ? outcome.FlowerSleep == 0
                        : outcome.FlowerSleep % economy.FlowersPerChipReward == 0),
                $"Players[{player.Id}] has an invalid Flower reward.");
            Require(outcome.FinalHavenSleep == 0
                    || outcome.Day == DuckMatchSettings.StandardDays
                    && outcome.FinalHavenSleep == economy.FinalShelterReward,
                $"Players[{player.Id}] has an invalid final-shelter reward.");
            Require(outcome.RestlessNightPenalty == 0
                    || outcomeEvent == DuckWorldEventType.RestlessNight
                    && outcome.RestlessNightPenalty == economy.RestlessNightPenalty,
                $"Players[{player.Id}] has an invalid Restless Night penalty.");
            var expectedCollectiveReward = outcomeEvent == DuckWorldEventType.AllTuckedIn ? economy.AllTuckedInReward
                : outcomeEvent == DuckWorldEventType.HomeBeforeDark ? economy.HomeBeforeDarkReward
                : outcomeEvent == DuckWorldEventType.SharedSupper ? economy.SharedSupperReward
                : 0;
            Require(outcome.CollectiveEventSleep == 0 || outcome.CollectiveEventSleep == expectedCollectiveReward,
                $"Players[{player.Id}] has an invalid collective-event reward.");
            Require(outcome.FlockSleep >= 0 && outcome.FlockSleep <= economy.FlockLeaderRewardLimit
                    && (economy.FlockLeaderPerCompanionReward == 0
                        ? outcome.FlockSleep == 0
                        : outcome.FlockSleep % economy.FlockLeaderPerCompanionReward == 0),
                $"Players[{player.Id}] has an invalid flock reward.");
            Require(outcome.PebblesPenalty == 0 || outcome.PebblesPenalty == economy.PebblesPenalty,
                $"Players[{player.Id}] has an invalid Pebbles penalty.");
            Require(outcome.SleepBeforeWear == outcome.PrintedSleep + outcome.FlowerSleep
                    + outcome.FinalHavenSleep - outcome.RestlessNightPenalty
                    + outcome.CollectiveEventSleep + outcome.GloriousSunshineSleep
                    + outcome.FlockSleep - outcome.PebblesPenalty,
                $"Players[{player.Id}] has an inconsistent Night reward breakdown.");
            Require(outcome.FrozenSleep == outcome.SleepBeforeWear
                    || outcome.FrozenSleep == outcome.SleepBeforeWear / economy.WearOutDivisor,
                $"Players[{player.Id}] has an invalid worn-out reward total.");
            Require(outcome.TotalTwigsEarned == outcome.PrintedTwigs + outcome.ReedsTwigs
                    + outcome.EventTwigs - outcome.BramblesPenalty,
                $"Players[{player.Id}] has an inconsistent Night Twig breakdown.");
            Require(outcome.DreamTwigs == (outcome.Day == DuckMatchSettings.StandardDays
                    ? economy.ConvertFinalRewardToDreamTwigs(outcome.FrozenSleep) + (outcome.IsMostRested ? 1 : 0) : 0),
                $"Players[{player.Id}] has an inconsistent Dream Twig conversion.");
            if (player.IsSleepFrozen)
                Require(outcome.Day == save.Day && player.FrozenSleep == outcome.FrozenSleep,
                    $"Players[{player.Id}] frozen Sleep does not match the current Night outcome.");
            else
                Require(player.FrozenSleep == 0 && player.RemainingSleep == 0,
                    $"Players[{player.Id}] retained spendable Sleep outside its resolved Night.");
        }

        private static void ValidateHistory(DuckSaveData save, ISet<string> playerIds)
        {
            Require(save.History != null, "History is required.");
            foreach (var entry in save.History)
                Require(entry != null && entry.Day >= 1 && entry.Day <= save.Day
                        && entry.ActorId != null
                        && (entry.ActorId.Length == 0 || playerIds.Contains(entry.ActorId))
                        && !string.IsNullOrWhiteSpace(entry.Message),
                    "History contains an invalid entry.");
        }

        private static void ValidateAwards(DuckSaveData save, ISet<string> playerIds)
        {
            Require(save.PublicAwards != null, "PublicAwards are required.");
            foreach (var award in save.PublicAwards)
                Require(award != null && award.Day >= 1 && award.Day <= save.Day
                        && !string.IsNullOrWhiteSpace(award.DefinitionId)
                        && award.PlayerIds != null && award.PlayerIds.Count > 0
                        && award.PlayerIds.Distinct(StringComparer.Ordinal).Count() == award.PlayerIds.Count
                        && award.PlayerIds.All(playerIds.Contains)
                        && award.Sleep >= 0 && award.Twigs >= 0 && award.Feathers >= 0,
                    "PublicAwards contains an invalid entry.");
        }

        private static void ValidateCommits(DuckSaveData save, ISet<string> playerIds)
        {
            Require(save.FinalDayCommits != null, "FinalDayCommits are required.");
            if (save.FinalDayCommits.Count == 0) return;
            Require(save.Day == DuckMatchSettings.StandardDays && save.Phase == DuckPhase.Adventure
                    && save.FinalDayDecisionBeat >= 1,
                "Final-Day commitments can exist only during a positive Day 10 decision beat.");
            Require(save.FinalDayCommits.All(commit => commit != null
                    && commit.Beat == save.FinalDayDecisionBeat
                    && playerIds.Contains(commit.PlayerId)
                    && (commit.ActionKind == GameActionKind.Explore || commit.ActionKind == GameActionKind.Settle))
                    && save.FinalDayCommits.Select(commit => commit.PlayerId).Distinct(StringComparer.Ordinal).Count()
                        == save.FinalDayCommits.Count,
                "FinalDayCommits contains an invalid or duplicate commitment.");
            var activeCount = save.Players.Count(player => !player.HasFinishedDay);
            Require(save.FinalDayCommits.Count < activeCount,
                "A complete final-Day commitment cohort should already have resolved atomically.");
            foreach (var commit in save.FinalDayCommits)
            {
                var player = save.Players.Single(candidate => candidate.Id == commit.PlayerId);
                Require(!player.HasFinishedDay && !player.IsWornOut,
                    "A finished duck cannot have a pending final-Day commitment.");
                Require(commit.ActionKind != GameActionKind.Explore || player.BagPhysicalChipIds.Count > 0,
                    "A pending Explore commitment needs a remaining chip.");
                Require(commit.ActionKind != GameActionKind.Settle || player.PlacedChips.Count > 0,
                    "A pending Settle commitment needs a placed chip.");
            }
        }

        private static void ValidateRevisions(DuckSaveData save, ISet<string> playerIds)
        {
            Require(save.CommandRevisions != null && save.CommandRevisions.Count == playerIds.Count,
                "CommandRevisions must contain every player.");
            Require(save.CommandRevisions.All(revision => revision != null
                    && playerIds.Contains(revision.PlayerId)
                    && revision.Revision >= 0 && revision.Revision < long.MaxValue)
                    && save.CommandRevisions.Select(revision => revision.PlayerId)
                        .Distinct(StringComparer.Ordinal).Count() == playerIds.Count,
                "CommandRevisions contains a missing, duplicate or invalid revision.");
        }

        private static void ValidateGoose(DuckSaveData save)
        {
            Require(save.DayFiveGooseAdded == (save.Day >= 5),
                "DayFiveGooseAdded must match the calendar position.");
            foreach (var player in save.Players)
            {
                var gooseCount = player.Inventory.Count(chip => chip.DefinitionId == "grumpy_goose");
                Require(gooseCount == (save.DayFiveGooseAdded ? 1 : 0),
                    $"Players[{player.Id}] must contain the calendar's exact Grumpy Goose count.");
            }
        }

        private static void ValidateFinalResult(DuckSaveData save, ISet<string> playerIds)
        {
            Require((save.Phase == DuckPhase.Finished) == (save.FinalResult != null),
                "FinalResult must exist exactly when the match is Finished.");
            if (save.FinalResult == null) return;
            Require(save.FinalResult.Standings != null && save.FinalResult.WinnerIds != null,
                "FinalResult standings and winners are required.");
            Require(save.FinalResult.Standings.Count == playerIds.Count
                    && save.FinalResult.Standings.All(standing => standing != null)
                    && save.FinalResult.Standings.Select(standing => standing.PlayerId)
                        .ToHashSet(StringComparer.Ordinal).SetEquals(playerIds),
                "FinalResult must contain one standing per player.");
            Require(save.FinalResult.WinnerIds.Count > 0
                    && save.FinalResult.WinnerIds.Distinct(StringComparer.Ordinal).Count()
                        == save.FinalResult.WinnerIds.Count
                    && save.FinalResult.WinnerIds.All(playerIds.Contains),
                "FinalResult winners are missing or invalid.");
            foreach (var standing in save.FinalResult.Standings)
            {
                var player = save.Players.Single(candidate => candidate.Id == standing.PlayerId);
                Require(standing.PlayerName == player.Name && standing.Rank >= 1
                        && standing.TotalTwigs == player.TotalTwigs
                        && standing.FrozenNightTenSleep == player.FrozenSleep
                        && player.LastNightOutcome != null
                        && standing.DreamTwigs == player.LastNightOutcome.DreamTwigs
                        && standing.IsWinner == save.FinalResult.WinnerIds.Contains(standing.PlayerId),
                    "FinalResult contains a standing inconsistent with authoritative player state.");
            }
            var expected = save.Players
                .OrderByDescending(player => player.TotalTwigs)
                .ThenByDescending(player => player.FrozenSleep)
                .ThenBy(player => player.Id, StringComparer.Ordinal).ToArray();
            var top = expected[0];
            for (var index = 0; index < expected.Length; index++)
            {
                var standing = save.FinalResult.Standings[index];
                var expectedRank = index == 0 || expected[index].TotalTwigs != expected[index - 1].TotalTwigs
                    || expected[index].FrozenSleep != expected[index - 1].FrozenSleep ? index + 1
                    : save.FinalResult.Standings[index - 1].Rank;
                Require(standing.PlayerId == expected[index].Id && standing.Rank == expectedRank
                        && standing.IsWinner == (standing.TotalTwigs == top.TotalTwigs
                            && standing.FrozenNightTenSleep == top.FrozenSleep),
                    "FinalResult ordering, rank or winner flags are inconsistent.");
            }
        }

        private static void ValidatePhase(DuckSaveData save)
        {
            if (save.Phase == DuckPhase.Adventure)
            {
                Require(save.Players.Any(player => !player.HasFinishedDay)
                        && save.Players.All(player => !player.IsSleepFrozen && !player.HasFinishedDream),
                    "Adventure players cannot retain current-Day Dream state.");
            }
            else if (save.Phase == DuckPhase.Night)
            {
                Require(save.Day < DuckMatchSettings.StandardDays
                        && save.Players.All(player => player.HasFinishedDay && player.IsSleepFrozen)
                        && save.Players.Any(player => !player.HasFinishedDream),
                    "Night requires every duck's resolved Adventure and frozen Sleep.");
            }
            else if (save.Phase == DuckPhase.DayComplete)
            {
                Require(save.Day < DuckMatchSettings.StandardDays
                        && save.Players.All(player => player.HasFinishedDay && player.HasFinishedDream
                            && player.IsSleepFrozen && player.RemainingSleep == 0),
                    "DayComplete requires every duck to finish Dream choices.");
            }
            else if (save.Phase == DuckPhase.Finished)
            {
                Require(save.Day == DuckMatchSettings.StandardDays
                        && save.Players.All(player => player.HasFinishedDay && player.HasFinishedDream
                            && player.IsSleepFrozen && player.RemainingSleep == 0),
                    "Finished requires a completely resolved Day 10.");
            }
            if (save.Day == DuckMatchSettings.StandardDays
                && (save.Phase == DuckPhase.Adventure || save.Phase == DuckPhase.Finished))
                Require(save.FinalDayDecisionBeat >= 1,
                    "Day 10 Adventure and its frozen result require a positive final decision beat.");
            else
                Require(save.FinalDayDecisionBeat == 0,
                    "FinalDayDecisionBeat must be zero before Day 10 Adventure.");
        }

        private static void NonNegative(int value, string path)
        {
            Require(value >= 0, path + " cannot be negative.");
        }

        private static void Require([DoesNotReturnIf(false)] bool condition, string message)
        {
            if (!condition) throw new DuckSaveValidationException(message);
        }
    }

    public sealed class DuckSaveValidationException : ArgumentException
    {
        internal DuckSaveValidationException(string message) : base(message) { }
    }
}
