using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Match;
using GatheringSeason.Core.Randomness;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Authoritative Duck profile state behind the shared issued-command boundary.</summary>
    internal sealed partial class DuckMatchRuntime : IMatchRuntime<DuckMatchView>
    {
        private readonly ResumableRandomSource _random;

        private DuckMatchRuntime(int seed, DuckMatchSettings settings, int rulesRevision)
        {
            State = new DuckMatchState(settings, rulesRevision)
            {
                Day = 1,
                Phase = DuckPhase.Adventure,
                CurrentEventIndex = 0,
                FinalDayDecisionBeat = 0,
                NextPhysicalChipId = 1
            };
            Rules = DuckRules.ForRulesRevision(State.RulesRevision);
            _random = new ResumableRandomSource(seed);

            State.WorldEventDeckDefinitionIds.AddRange(Rules.WorldEvents.Select(item => item.DefinitionId));
            ShuffleInPlace(State.WorldEventDeckDefinitionIds);
            for (var seat = 0; seat < settings.PlayerCount; seat++)
                State.Players.Add(CreatePlayer(DuckPlayerSeats.Id(seat), DuckPlayerSeats.Name(seat)));
            if (CurrentEvent.EventType == DuckWorldEventType.FriendlyGuide)
                foreach (var player in State.Players) player.GuideProtectionAvailable = true;
            State.RandomState = _random.CaptureState();
        }

        internal static DuckMatchRuntime Create(
            int seed,
            DuckMatchSettings? settings = null,
            int rulesRevision = DuckRules.CurrentRulesRevision)
        {
            return new DuckMatchRuntime(seed, settings ?? DuckMatchSettings.Standard, rulesRevision);
        }

        public string ActionWindow => $"duck:{State.Day}:{State.Phase}:{State.FinalDayDecisionBeat}";
        internal DuckRuleDefinitions Rules { get; }
        internal DuckMatchState State { get; }
        internal DuckWorldEventDefinition CurrentEvent =>
            Rules.WorldEvent(State.WorldEventDeckDefinitionIds[State.CurrentEventIndex]);

        public DuckMatchView GetSnapshot(string playerId)
        {
            var viewer = Player(playerId);
            var ownInventoryById = viewer.Inventory.ToDictionary(chip => chip.PhysicalChipId);

            var players = State.Players.Select(player => new DuckPlayerView(
                player.Id,
                player.Name,
                player.TotalTwigs,
                player.PermanentFeatherTrail,
                player.DayReedsTwigs,
                player.DayEventTwigs,
                player.Position,
                player.Exhaustion,
                player.SafeExhaustionMaximum,
                player.ActiveFlock,
                player.SplashProtectionArmed,
                player.LogSlowdownPending,
                player.GuideProtectionAvailable,
                player.HasGloriousSunshineChoice,
                player.GloriousSunshineBenefit,
                player.FlowersPlaced,
                player.FrozenSleep,
                player.IsSleepFrozen,
                player.RemainingSleep,
                player.PendingMostRestedStep,
                player.ActiveMostRestedStep,
                player.EffectiveStart,
                player.HasFinishedDay,
                player.IsWornOut,
                player.BagPhysicalChipIds.Count,
                player.Inventory.Count,
                player.PlacedHelpfulTypes.OrderBy(item => item),
                player.PlacedChips.Select(chip => PlacedChipView(player, chip)),
                player.PurchasedEncounterDefinitionIds,
                player.PurchasedShopTypes.OrderBy(item => item),
                player.LastNightOutcome,
                player.HasFinishedDream,
                player.DawnTwigDeficit,
                player.DawnFeathersAwarded,
                DuckDreamHandler.PurchaseLimitForDay(State.Day)));

            var ownBag = viewer.BagPhysicalChipIds
                .OrderBy(id => id)
                .Select(id => PhysicalChipView(ownInventoryById[id]));
            var ownInventory = viewer.Inventory
                .OrderBy(chip => chip.PhysicalChipId)
                .Select(PhysicalChipView);
            var knownNext = viewer.KnownNextPhysicalChipIds
                .Select(id => PhysicalChipView(ownInventoryById[id]));
            var history = State.History.Select(entry => new DuckHistoryEntry(entry.Day, entry.ActorId, entry.Message));
            var awards = State.PublicAwards.Select(award => new DuckPublicAwardView(
                award.Day, award.DefinitionId, award.PlayerIds, award.Sleep, award.Twigs, award.Feathers));

            return new DuckMatchView(
                Rules,
                State.Settings,
                State.Day,
                State.Phase,
                viewer.Id,
                CurrentEvent,
                players,
                ownBag,
                ownInventory,
                knownNext,
                Rules.ShopOffers,
                history,
                awards,
                DuckNightResolver.Preview(State, Rules, viewer),
                State.FinalDayDecisionBeat,
                State.FinalDayCommits.Count > 0,
                State.FinalResult);
        }

        public IReadOnlyList<GameAction> GetLegalActions(string playerId)
        {
            var player = Player(playerId);
            switch (State.Phase)
            {
                case DuckPhase.Adventure: return DuckAdventureHandler.GetLegalActions(this, player);
                case DuckPhase.Night: return DuckDreamHandler.GetLegalActions(State, player, Rules);
                case DuckPhase.DayComplete when State.Day < DuckMatchSettings.StandardDays:
                    return DuckMatchView.Freeze(new[] { new GameAction(
                        "next-day", GameActionKind.NextDay, "Start Day " + (State.Day + 1)) });
                default: return new ReadOnlyCollection<GameAction>(Array.Empty<GameAction>());
            }
        }

        public DuckMatchView Execute(string playerId, GameAction action)
        {
            var player = Player(playerId);
            if (action == null) throw new ArgumentNullException(nameof(action));
            var legal = GetLegalActions(playerId).SingleOrDefault(candidate =>
                string.Equals(candidate.Id, action.Id, StringComparison.Ordinal));
            if (legal == null)
                throw new InvalidOperationException("Action '" + action.Id + "' is not legal for " + playerId + " in " + State.Phase + ".");
            switch (State.Phase)
            {
                case DuckPhase.Adventure: DuckAdventureHandler.Execute(this, player, legal); break;
                case DuckPhase.Night: DuckDreamHandler.Execute(State, player, Rules, legal); break;
                case DuckPhase.DayComplete: DuckDayPreparation.BeginNextDay(this); break;
                default: throw new InvalidOperationException("No Duck action can be executed during " + State.Phase + ".");
            }
            return GetSnapshot(playerId);
        }

        internal DuckPlayerState Player(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            return State.Players.SingleOrDefault(player => string.Equals(player.Id, playerId, StringComparison.Ordinal))
                ?? throw new ArgumentException("Unknown player ID '" + playerId + "'.", nameof(playerId));
        }

        internal int NextRandomInt(int exclusiveMax)
        {
            var result = _random.NextInt(exclusiveMax);
            State.RandomState = _random.CaptureState();
            return result;
        }

        internal void ShuffleInPlace<T>(IList<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            for (var index = values.Count - 1; index > 0; index--)
            {
                var swapIndex = _random.NextInt(index + 1);
                var value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
            State.RandomState = _random.CaptureState();
        }

        internal void AddHistory(string actorId, string message)
        {
            State.History.Add(new DuckHistoryState(State.Day, actorId, message));
        }

        internal void AddPublicAward(DuckPublicAwardState award)
        {
            State.PublicAwards.Add(award ?? throw new ArgumentNullException(nameof(award)));
        }

        private DuckPlayerState CreatePlayer(string id, string name)
        {
            var player = new DuckPlayerState(id, name, State.Settings.StartingFeathers);
            foreach (var definition in Rules.OpeningBag)
            {
                var chip = new DuckPhysicalChipState(State.NextPhysicalChipId++, definition.DefinitionId);
                player.Inventory.Add(chip);
                player.BagPhysicalChipIds.Add(chip.PhysicalChipId);
            }
            ShuffleInPlace(player.BagPhysicalChipIds);
            return player;
        }

        private static DuckPhysicalChipView PhysicalChipView(DuckPhysicalChipState chip)
        {
            return new DuckPhysicalChipView(chip.PhysicalChipId, chip.DefinitionId);
        }

        private static DuckPlacedChipView PlacedChipView(DuckPlayerState player, DuckPlacedChipState placed)
        {
            var chip = player.Inventory.Single(candidate => candidate.PhysicalChipId == placed.PhysicalChipId);
            return new DuckPlacedChipView(chip.PhysicalChipId, chip.DefinitionId, placed.Position, placed.NuisanceSuppressed);
        }
    }
}
