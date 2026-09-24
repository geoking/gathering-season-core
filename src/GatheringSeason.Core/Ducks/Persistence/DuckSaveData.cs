using System.Collections.Generic;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.Persistence
{
    /// <summary>
    /// Serializer-neutral authoritative Duck save data. Hosts own encoding and storage;
    /// Core owns capture, validation and restoration.
    /// </summary>
    public sealed class DuckSaveData
    {
        public int FormatVersion;
        public string ProfileId = string.Empty;
        public int RulesVersion;
        public DuckSaveSettingsData Settings = new DuckSaveSettingsData();
        public int Day;
        public DuckPhase Phase;
        public int CurrentEventIndex;
        public int FinalDayDecisionBeat;
        public int NextPhysicalChipId;
        public DuckRandomSaveData Random = new DuckRandomSaveData();
        public List<string> WorldEventDeckDefinitionIds = new List<string>();
        public List<DuckPlayerSaveData> Players = new List<DuckPlayerSaveData>();
        public List<DuckHistorySaveData> History = new List<DuckHistorySaveData>();
        public List<DuckPublicAwardSaveData> PublicAwards = new List<DuckPublicAwardSaveData>();
        public List<DuckFinalDayCommitSaveData> FinalDayCommits = new List<DuckFinalDayCommitSaveData>();
        public List<DuckCommandRevisionSaveData> CommandRevisions = new List<DuckCommandRevisionSaveData>();
        public bool DayFiveGooseAdded;
        public DuckFinalResultSaveData? FinalResult;
    }

    public sealed class DuckSaveSettingsData
    {
        public int Days;
        public int StartingFeathers;
        public int PlayerCount;
        public string? WishSetId;
    }

    public sealed class DuckRandomSaveData
    {
        public string Algorithm = string.Empty;
        public ulong State;
        public ulong Increment;
    }

    public sealed class DuckPlayerSaveData
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int PermanentFeatherTrail;
        public int TotalTwigs;
        public int DayReedsTwigs;
        public int DayEventTwigs;
        public int Position;
        public int Exhaustion;
        public int SafeExhaustionMaximum;
        public int ActiveFlock;
        public bool SplashProtectionArmed;
        public bool LogSlowdownPending;
        public bool GuideProtectionAvailable;
        public bool PocketDriftwoodAwarded;
        public bool HasGloriousSunshineChoice;
        public DuckGloriousSunshineBenefit GloriousSunshineBenefit;
        public int FlowersPlaced;
        public int FrozenSleep;
        public bool IsSleepFrozen;
        public int RemainingSleep;
        public bool PendingMostRestedStep;
        public bool ActiveMostRestedStep;
        public int EffectiveStart;
        public bool HasFinishedDay;
        public bool HasFinishedDream;
        public bool IsWornOut;
        public int DawnTwigDeficit;
        public int DawnFeathersAwarded;
        public List<DuckPhysicalChipSaveData> Inventory = new List<DuckPhysicalChipSaveData>();
        public List<int> BagPhysicalChipIds = new List<int>();
        public List<int> KnownNextPhysicalChipIds = new List<int>();
        public List<DuckPlacedChipSaveData> PlacedChips = new List<DuckPlacedChipSaveData>();
        public List<DuckEncounterType> PlacedHelpfulTypes = new List<DuckEncounterType>();
        public List<string> PurchasedEncounterDefinitionIds = new List<string>();
        public List<DuckEncounterType> PurchasedShopTypes = new List<DuckEncounterType>();
        public DuckNightOutcomeSaveData? LastNightOutcome;
    }

    public sealed class DuckPhysicalChipSaveData
    {
        public int PhysicalChipId;
        public string DefinitionId = string.Empty;
    }

    public sealed class DuckPlacedChipSaveData
    {
        public int PhysicalChipId;
        public int Position;
        public bool NuisanceSuppressed;
    }

    public sealed class DuckHistorySaveData
    {
        public int Day;
        public string ActorId = string.Empty;
        public string Message = string.Empty;
    }

    public sealed class DuckPublicAwardSaveData
    {
        public int Day;
        public string DefinitionId = string.Empty;
        public List<string> PlayerIds = new List<string>();
        public int Sleep;
        public int Twigs;
        public int Feathers;
    }

    public sealed class DuckFinalDayCommitSaveData
    {
        public int Beat;
        public string PlayerId = string.Empty;
        public GameActionKind ActionKind;
    }

    public sealed class DuckCommandRevisionSaveData
    {
        public string PlayerId = string.Empty;
        public long Revision;
    }

    public sealed class DuckNightOutcomeSaveData
    {
        public int Day;
        public int PrintedSleep;
        public int PrintedTwigs;
        public int ReedsTwigs;
        public int EventTwigs;
        public int BramblesPenalty;
        public int FlowerSleep;
        public int FinalHavenSleep;
        public int RestlessNightPenalty;
        public int CollectiveEventSleep;
        public int GloriousSunshineSleep;
        public int FlockSleep;
        public int PebblesPenalty;
        public int SleepBeforeWear;
        public int FrozenSleep;
        public int TotalTwigsEarned;
        public int FeathersAwarded;
        public bool IsMostRested;
        public int NextDayTemporaryStep;
        public int DreamTwigs;
    }

    public sealed class DuckFinalResultSaveData
    {
        public List<DuckFinalStandingSaveData> Standings = new List<DuckFinalStandingSaveData>();
        public List<string> WinnerIds = new List<string>();
    }

    public sealed class DuckFinalStandingSaveData
    {
        public string PlayerId = string.Empty;
        public string PlayerName = string.Empty;
        public int Rank;
        public int TotalTwigs;
        public int FrozenNightTenSleep;
        public int DreamTwigs;
        public bool IsWinner;
    }
}
