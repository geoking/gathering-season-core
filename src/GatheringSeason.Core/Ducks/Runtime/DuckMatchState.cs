using System;
using System.Collections.Generic;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Match;
using GatheringSeason.Core.Randomness;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>
    /// Authoritative save fields for a Duck match. Command authorization is deliberately absent:
    /// restored sessions issue a fresh command scope. A C5 adapter may serialize these plain values.
    /// </summary>
    internal sealed class DuckMatchState
    {
        internal DuckMatchState(
            DuckMatchSettings settings,
            int rulesRevision = DuckRules.CurrentRulesRevision)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            DuckRules.ForRulesRevision(rulesRevision);
            RulesRevision = rulesRevision;
        }

        public DuckMatchSettings Settings { get; }
        public string ProfileId => DuckRules.V1.ProfileId;
        public int RulesRevision { get; }
        public int Day { get; set; }
        public DuckPhase Phase { get; set; }
        public int CurrentEventIndex { get; set; }
        public bool DayFiveGooseAdded { get; set; }
        public int FinalDayDecisionBeat { get; set; }
        public int NextPhysicalChipId { get; set; }
        public RandomState RandomState { get; set; } = null!;
        public List<string> WorldEventDeckDefinitionIds { get; } = new List<string>();
        public List<DuckPlayerState> Players { get; } = new List<DuckPlayerState>();
        public List<DuckHistoryState> History { get; } = new List<DuckHistoryState>();
        public List<DuckPublicAwardState> PublicAwards { get; } = new List<DuckPublicAwardState>();
        public List<DuckFinalDayCommitState> FinalDayCommits { get; } = new List<DuckFinalDayCommitState>();
        public DuckFinalResult? FinalResult { get; set; }
    }

    internal sealed class DuckPlayerState
    {
        internal DuckPlayerState(string id, string name, int startingFeathers)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A player needs an ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A player needs a name.", nameof(name));
            if (startingFeathers < 0 || startingFeathers > 3) throw new ArgumentOutOfRangeException(nameof(startingFeathers));

            Id = id;
            Name = name;
            PermanentFeatherTrail = startingFeathers;
            EffectiveStart = startingFeathers;
            Position = startingFeathers;
            SafeExhaustionMaximum = 5;
        }

        public string Id { get; }
        public string Name { get; }
        public int PermanentFeatherTrail { get; set; }
        /// <summary>Cumulative Nest Twigs, including immediate Reeds and event gains.</summary>
        public int TotalTwigs { get; set; }
        public int DayReedsTwigs { get; set; }
        public int DayEventTwigs { get; set; }
        public int Position { get; set; }
        public int Exhaustion { get; set; }
        public int SafeExhaustionMaximum { get; set; }
        public int ActiveFlock { get; set; }
        public bool SplashProtectionArmed { get; set; }
        public bool LogSlowdownPending { get; set; }
        public bool GuideProtectionAvailable { get; set; }
        public bool PocketDriftwoodAwarded { get; set; }
        public bool HasGloriousSunshineChoice { get; set; }
        public DuckGloriousSunshineBenefit GloriousSunshineBenefit { get; set; }
        public int FlowersPlaced { get; set; }
        public int FrozenSleep { get; set; }
        public bool IsSleepFrozen { get; set; }
        public int RemainingSleep { get; set; }
        public bool PendingMostRestedStep { get; set; }
        public bool ActiveMostRestedStep { get; set; }
        public int EffectiveStart { get; set; }
        public bool HasFinishedDay { get; set; }
        public bool HasFinishedDream { get; set; }
        public bool IsWornOut { get; set; }
        public int DawnTwigDeficit { get; set; }
        public int DawnFeathersAwarded { get; set; }
        public List<DuckPhysicalChipState> Inventory { get; } = new List<DuckPhysicalChipState>();
        public List<int> BagPhysicalChipIds { get; } = new List<int>();
        public List<int> KnownNextPhysicalChipIds { get; } = new List<int>();
        public List<DuckPlacedChipState> PlacedChips { get; } = new List<DuckPlacedChipState>();
        public HashSet<DuckEncounterType> PlacedHelpfulTypes { get; } = new HashSet<DuckEncounterType>();
        public List<string> PurchasedEncounterDefinitionIds { get; } = new List<string>();
        public HashSet<DuckEncounterType> PurchasedShopTypes { get; } = new HashSet<DuckEncounterType>();
        public DuckNightOutcome? LastNightOutcome { get; set; }
    }

    internal sealed class DuckPhysicalChipState
    {
        internal DuckPhysicalChipState(int physicalChipId, string definitionId)
        {
            if (physicalChipId <= 0) throw new ArgumentOutOfRangeException(nameof(physicalChipId));
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("A physical chip needs a definition ID.", nameof(definitionId));
            PhysicalChipId = physicalChipId;
            DefinitionId = definitionId;
        }

        public int PhysicalChipId { get; }
        public string DefinitionId { get; }
    }

    internal sealed class DuckPlacedChipState
    {
        internal DuckPlacedChipState(int physicalChipId, int position, bool nuisanceSuppressed)
        {
            if (physicalChipId <= 0) throw new ArgumentOutOfRangeException(nameof(physicalChipId));
            if (position < 1 || position > 43) throw new ArgumentOutOfRangeException(nameof(position));
            PhysicalChipId = physicalChipId;
            Position = position;
            NuisanceSuppressed = nuisanceSuppressed;
        }

        public int PhysicalChipId { get; }
        public int Position { get; set; }
        public bool NuisanceSuppressed { get; set; }
    }

    internal sealed class DuckHistoryState
    {
        internal DuckHistoryState(int day, string actorId, string message)
        {
            if (day < 1 || day > DuckMatchSettings.StandardDays) throw new ArgumentOutOfRangeException(nameof(day));
            Day = day;
            ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public int Day { get; }
        public string ActorId { get; }
        public string Message { get; }
    }

    internal sealed class DuckPublicAwardState
    {
        internal DuckPublicAwardState(int day, string definitionId, IEnumerable<string> playerIds, int sleep, int twigs, int feathers)
        {
            if (day < 1 || day > DuckMatchSettings.StandardDays) throw new ArgumentOutOfRangeException(nameof(day));
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("An award needs a definition ID.", nameof(definitionId));
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            if (sleep < 0) throw new ArgumentOutOfRangeException(nameof(sleep));
            if (twigs < 0) throw new ArgumentOutOfRangeException(nameof(twigs));
            if (feathers < 0) throw new ArgumentOutOfRangeException(nameof(feathers));
            Day = day;
            DefinitionId = definitionId;
            PlayerIds.AddRange(playerIds);
            Sleep = sleep;
            Twigs = twigs;
            Feathers = feathers;
        }

        public int Day { get; }
        public string DefinitionId { get; }
        public List<string> PlayerIds { get; } = new List<string>();
        public int Sleep { get; }
        public int Twigs { get; }
        public int Feathers { get; }
    }

    /// <summary>Serializable hidden choice for one Day 10 decision beat.</summary>
    internal sealed class DuckFinalDayCommitState
    {
        internal DuckFinalDayCommitState(int beat, string playerId, GameActionKind actionKind)
        {
            if (beat < 1) throw new ArgumentOutOfRangeException(nameof(beat));
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A commitment needs a player ID.", nameof(playerId));
            if (actionKind != GameActionKind.Explore && actionKind != GameActionKind.Settle)
                throw new ArgumentException("A final-Day commitment must be Explore or Settle.", nameof(actionKind));
            Beat = beat;
            PlayerId = playerId;
            ActionKind = actionKind;
        }

        public int Beat { get; }
        public string PlayerId { get; }
        public GameActionKind ActionKind { get; }
    }
}
