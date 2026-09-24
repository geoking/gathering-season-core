using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Detached immutable Duck observation. Bag and preview details belong only to ViewerId.</summary>
    public sealed class DuckMatchView
    {
        public string ProfileId => Rules.ProfileId;
        internal DuckMatchView(
            DuckRuleDefinitions rules,
            DuckMatchSettings settings,
            int day,
            DuckPhase phase,
            string viewerId,
            DuckWorldEventDefinition currentEvent,
            IEnumerable<DuckPlayerView> players,
            IEnumerable<DuckPhysicalChipView> ownBag,
            IEnumerable<DuckPhysicalChipView> ownInventory,
            IEnumerable<DuckPhysicalChipView> knownNextChips,
            IEnumerable<DuckShopOffer> shopOffers,
            IEnumerable<DuckHistoryEntry> history,
            IEnumerable<DuckPublicAwardView> publicAwards,
            DuckRestPreview? restPreview,
            int finalDayDecisionBeat,
            bool awaitingFinalDayDecisions,
            DuckFinalResult? finalResult)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (day < 1 || day > DuckMatchSettings.StandardDays) throw new ArgumentOutOfRangeException(nameof(day));
            Day = day;
            NestLevel = DuckDreamHandler.NestLevelForDay(day);
            Phase = phase;
            ViewerId = viewerId ?? throw new ArgumentNullException(nameof(viewerId));
            CurrentEvent = currentEvent ?? throw new ArgumentNullException(nameof(currentEvent));
            Players = Freeze(players);
            OwnBag = Freeze(ownBag);
            OwnInventory = Freeze(ownInventory);
            KnownNextChips = Freeze(knownNextChips);
            ShopOffers = Freeze(shopOffers);
            History = Freeze(history);
            PublicAwards = Freeze(publicAwards);
            RestPreview = restPreview;
            FinalDayDecisionBeat = finalDayDecisionBeat;
            AwaitingFinalDayDecisions = awaitingFinalDayDecisions;
            FinalResult = finalResult;
        }

        public DuckRuleDefinitions Rules { get; }
        public DuckEconomyDefinition Economy => Rules.Economy;
        public int RulesRevision => Rules.RulesRevision;
        public DuckMatchSettings Settings { get; }
        public int Day { get; }
        public int NestLevel { get; }
        public DuckPhase Phase { get; }
        public string ViewerId { get; }
        public DuckWorldEventDefinition CurrentEvent { get; }
        public IReadOnlyList<DuckPlayerView> Players { get; }
        /// <summary>Remaining chips sorted by physical ID; this never exposes their private draw order.</summary>
        public IReadOnlyList<DuckPhysicalChipView> OwnBag { get; }
        public IReadOnlyList<DuckPhysicalChipView> OwnInventory { get; }
        /// <summary>The viewer's exact known future chips, in draw order.</summary>
        public IReadOnlyList<DuckPhysicalChipView> KnownNextChips { get; }
        public IReadOnlyList<DuckShopOffer> ShopOffers { get; }
        public IReadOnlyList<DuckHistoryEntry> History { get; }
        public IReadOnlyList<DuckPublicAwardView> PublicAwards { get; }
        public DuckRestPreview? RestPreview { get; }
        public int FinalDayDecisionBeat { get; }
        public bool AwaitingFinalDayDecisions { get; }
        public DuckFinalResult? FinalResult { get; }

        internal static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return new ReadOnlyCollection<T>(values.ToList());
        }
    }

    public sealed class DuckPlayerView
    {
        internal DuckPlayerView(
            string id,
            string name,
            int totalTwigs,
            int permanentFeatherTrail,
            int dayReedsTwigs,
            int dayEventTwigs,
            int position,
            int exhaustion,
            int safeExhaustionMaximum,
            int activeFlock,
            bool splashProtectionArmed,
            bool logSlowdownPending,
            bool guideProtectionAvailable,
            bool hasGloriousSunshineChoice,
            DuckGloriousSunshineBenefit gloriousSunshineBenefit,
            int flowersPlaced,
            int frozenSleep,
            bool isSleepFrozen,
            int remainingSleep,
            bool pendingMostRestedStep,
            bool activeMostRestedStep,
            int effectiveStart,
            bool hasFinishedDay,
            bool isWornOut,
            int bagCount,
            int inventoryCount,
            IEnumerable<DuckEncounterType> placedHelpfulTypes,
            IEnumerable<DuckPlacedChipView> placedChips,
            IEnumerable<string> purchasedEncounterDefinitionIds,
            IEnumerable<DuckEncounterType> purchasedShopTypes,
            DuckNightOutcome? lastNightOutcome,
            bool hasFinishedDream = false,
            int dawnTwigDeficit = 0,
            int dawnFeathersAwarded = 0,
            int purchaseLimit = 0)
        {
            Id = id;
            Name = name;
            TotalTwigs = totalTwigs;
            PermanentFeatherTrail = permanentFeatherTrail;
            DayReedsTwigs = dayReedsTwigs;
            DayEventTwigs = dayEventTwigs;
            Position = position;
            Exhaustion = exhaustion;
            SafeExhaustionMaximum = safeExhaustionMaximum;
            ActiveFlock = activeFlock;
            SplashProtectionArmed = splashProtectionArmed;
            LogSlowdownPending = logSlowdownPending;
            GuideProtectionAvailable = guideProtectionAvailable;
            HasGloriousSunshineChoice = hasGloriousSunshineChoice;
            GloriousSunshineBenefit = gloriousSunshineBenefit;
            FlowersPlaced = flowersPlaced;
            FrozenSleep = frozenSleep;
            IsSleepFrozen = isSleepFrozen;
            RemainingSleep = remainingSleep;
            PendingMostRestedStep = pendingMostRestedStep;
            ActiveMostRestedStep = activeMostRestedStep;
            EffectiveStart = effectiveStart;
            HasFinishedDay = hasFinishedDay;
            IsWornOut = isWornOut;
            BagCount = bagCount;
            InventoryCount = inventoryCount;
            PlacedHelpfulTypes = DuckMatchView.Freeze(placedHelpfulTypes);
            PlacedChips = DuckMatchView.Freeze(placedChips);
            PurchasedEncounterDefinitionIds = DuckMatchView.Freeze(purchasedEncounterDefinitionIds);
            PurchasedShopTypes = DuckMatchView.Freeze(purchasedShopTypes);
            LastNightOutcome = lastNightOutcome;
            HasFinishedDream = hasFinishedDream;
            DawnTwigDeficit = dawnTwigDeficit;
            DawnFeathersAwarded = dawnFeathersAwarded;
            PurchaseLimit = purchaseLimit;
        }

        public string Id { get; }
        public string Name { get; }
        public int TotalTwigs { get; }
        public int PermanentFeatherTrail { get; }
        public int DayReedsTwigs { get; }
        public int DayEventTwigs { get; }
        public int Position { get; }
        public int Exhaustion { get; }
        public int SafeExhaustionMaximum { get; }
        public int ActiveFlock { get; }
        public bool SplashProtectionArmed { get; }
        public bool LogSlowdownPending { get; }
        public bool GuideProtectionAvailable { get; }
        public bool HasGloriousSunshineChoice { get; }
        public DuckGloriousSunshineBenefit GloriousSunshineBenefit { get; }
        public int FlowersPlaced { get; }
        public int FrozenSleep { get; }
        public int FrozenReward => FrozenSleep;
        public int FrozenStars => FrozenSleep;
        public bool IsSleepFrozen { get; }
        public bool IsRewardFrozen => IsSleepFrozen;
        public int RemainingSleep { get; }
        public int RemainingReward => RemainingSleep;
        public int RemainingStars => RemainingSleep;
        public bool PendingMostRestedStep { get; }
        public bool ActiveMostRestedStep { get; }
        public int EffectiveStart { get; }
        public bool HasFinishedDay { get; }
        public bool IsWornOut { get; }
        public int BagCount { get; }
        public int InventoryCount { get; }
        public IReadOnlyList<DuckEncounterType> PlacedHelpfulTypes { get; }
        public IReadOnlyList<DuckPlacedChipView> PlacedChips { get; }
        public IReadOnlyList<string> PurchasedEncounterDefinitionIds { get; }
        public IReadOnlyList<DuckEncounterType> PurchasedShopTypes { get; }
        public DuckNightOutcome? LastNightOutcome { get; }
        public bool HasFinishedDream { get; }
        public int DawnTwigDeficit { get; }
        public int DawnFeathersAwarded { get; }
        public int PurchaseLimit { get; }
    }

    public sealed class DuckPhysicalChipView
    {
        internal DuckPhysicalChipView(int physicalChipId, string definitionId)
        {
            PhysicalChipId = physicalChipId;
            DefinitionId = definitionId;
        }

        public int PhysicalChipId { get; }
        public string DefinitionId { get; }
    }

    public sealed class DuckPlacedChipView
    {
        internal DuckPlacedChipView(int physicalChipId, string definitionId, int position, bool nuisanceSuppressed)
        {
            PhysicalChipId = physicalChipId;
            DefinitionId = definitionId;
            Position = position;
            NuisanceSuppressed = nuisanceSuppressed;
        }

        public int PhysicalChipId { get; }
        public string DefinitionId { get; }
        public int Position { get; }
        public bool NuisanceSuppressed { get; }
    }

    public sealed class DuckHistoryEntry
    {
        internal DuckHistoryEntry(int day, string actorId, string message)
        {
            Day = day;
            ActorId = actorId;
            Message = message;
        }

        public int Day { get; }
        public string ActorId { get; }
        public string Message { get; }
    }

    public sealed class DuckPublicAwardView
    {
        internal DuckPublicAwardView(
            int day,
            string definitionId,
            IEnumerable<string> playerIds,
            int sleep,
            int twigs,
            int feathers)
        {
            Day = day;
            DefinitionId = definitionId;
            PlayerIds = DuckMatchView.Freeze(playerIds);
            Sleep = sleep;
            Twigs = twigs;
            Feathers = feathers;
        }

        public int Day { get; }
        public string DefinitionId { get; }
        public IReadOnlyList<string> PlayerIds { get; }
        public int Sleep { get; }
        public int Reward => Sleep;
        public int Stars => Sleep;
        public int Twigs { get; }
        public int Feathers { get; }
    }
}
