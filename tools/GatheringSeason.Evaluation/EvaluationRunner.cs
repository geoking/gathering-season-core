using System.Diagnostics;
using System.Reflection;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using GatheringSeason.Evaluation.Policies;

namespace GatheringSeason.Evaluation;

public sealed class EvaluationRunner
{
    private static readonly string[] PlayerIds = { "human", "ai" };
    private const int MaximumActions = 4000;

    public EvaluationRunOutcome Run(EvaluationMatchRequest request, Action<DuckSaveData>? beforeDawn = null)
    {
        Validate(request);
        var match = MatchSession.CreateDuck(request.Seed);
        var policies = AssignPolicies(request);
        var days = new Dictionary<int, DayBuilder>();
        var traces = request.IncludeActions ? new List<EvaluationActionTrace>() : null;
        var timings = new Dictionary<string, TimingBuilder>(StringComparer.Ordinal);
        var lastDecisions = new Dictionary<string, EvaluationPolicyDecision>(StringComparer.Ordinal);
        var sequence = 0;

        CaptureDayStart(match, policies, days);
        for (var pass = 0; sequence < MaximumActions; pass++)
        {
            if (match.GetSnapshot("human").Phase == DuckPhase.Finished)
                return Complete(request, match, policies, days, timings, traces);

            var acted = false;
            var passView = match.GetSnapshot("human");
            foreach (var playerId in PlayerOrder(request.Schedule, passView.Day))
            {
                if (sequence >= MaximumActions)
                    throw new InvalidOperationException($"Evaluation exceeded the {MaximumActions}-action safety bound.");
                var legalActions = match.GetLegalActions(playerId);
                if (legalActions.Count == 0) continue;

                var observation = match.GetSnapshot(playerId);
                var policy = policies[playerId];
                var policyTimer = Stopwatch.StartNew();
                var decision = policy.Decide(observation, legalActions);
                policyTimer.Stop();
                if (decision?.Action == null || !legalActions.Any(action => ReferenceEquals(action, decision.Action)))
                    throw new InvalidOperationException(
                        $"Policy '{policy.Id}' must return the exact issued action object for {playerId}.");
                if (string.IsNullOrWhiteSpace(decision.Reason))
                    throw new InvalidOperationException($"Policy '{policy.Id}' returned an empty decision reason.");

                var policyMicros = Microseconds(policyTimer.ElapsedTicks);
                lastDecisions[playerId] = decision;
                RecordPurchaseBeforeExecution(days, observation, playerId, decision, policyMicros);
                RecordUnspentRewardBeforeFinish(days, observation, playerId, decision.Action);

                if (decision.Action.Kind == GameActionKind.NextDay)
                    beforeDawn?.Invoke(DuckSaves.Capture(match));
                var executeTimer = Stopwatch.StartNew();
                match.Execute(playerId, decision.Action);
                executeTimer.Stop();
                var executeMicros = Microseconds(executeTimer.ElapsedTicks);
                Timing(timings, policy.Id).Add(policyMicros, executeMicros);
                sequence++;

                var publicAfter = match.GetSnapshot("human");
                var hiddenCommit = observation.Day == DuckMatchSettings.StandardDays
                    && observation.Phase == DuckPhase.Adventure
                    && decision.Action.Kind is GameActionKind.Explore or GameActionKind.Settle
                    && (publicAfter.AwaitingFinalDayDecisions || publicAfter.FinalDayDecisionBeat > observation.FinalDayDecisionBeat);
                traces?.Add(new EvaluationActionTrace(
                    sequence, observation.Day, observation.Phase, playerId, policy.Id,
                    decision.Action.Id, decision.Action.Kind, decision.Action.DefinitionId,
                    decision.Action.Label, decision.Reason, policyMicros, executeMicros, hiddenCommit));

                UpdateAdventureProgress(publicAfter, days, lastDecisions);
                CaptureNightBoundary(publicAfter, days, lastDecisions);
                CaptureDreamEnd(match, publicAfter, days);
                if (publicAfter.Phase == DuckPhase.Adventure)
                    CaptureDayStart(match, policies, days);
                acted = true;
                var dayChanged = publicAfter.Day != observation.Day;

                if (publicAfter.Phase == DuckPhase.Finished)
                    return Complete(request, match, policies, days, timings, traces);
                // CLI deliberately continues its fixed human/AI foreach after
                // NextDay. Other schedules begin a fresh pass so their declared
                // lead order owns the first action of the new Day exactly once.
                if (dayChanged && request.Schedule != EvaluationSchedule.Cli)
                    break;
            }
            if (!acted)
                throw new InvalidOperationException("Evaluation stalled before final scoring: no player had an issued action.");
        }
        throw new InvalidOperationException($"Evaluation exceeded the {MaximumActions}-action safety bound.");
    }

    private static EvaluationRunOutcome Complete(
        EvaluationMatchRequest request,
        MatchSession<DuckMatchView> match,
        IReadOnlyDictionary<string, IEvaluationPolicy> policies,
        IReadOnlyDictionary<int, DayBuilder> days,
        IReadOnlyDictionary<string, TimingBuilder> timings,
        IReadOnlyList<EvaluationActionTrace>? traces)
    {
        var finalView = match.GetSnapshot("human");
        CaptureNightBoundary(finalView, days, new Dictionary<string, EvaluationPolicyDecision>());
        CaptureDreamEnd(match, finalView, days);
        var final = finalView.FinalResult ?? throw new InvalidOperationException("Finished match has no final result.");
        var result = new EvaluationMatchResult(
            2,
            request.SourceLabel,
            typeof(MatchSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MatchSession).Assembly.GetName().Version?.ToString() ?? "unknown",
            finalView.ProfileId,
            finalView.RulesRevision,
            finalView.Economy.CurrencyName,
            DuckSaves.CurrentFormatVersion,
            request.Seed,
            request.MatchIndex,
            new EvaluationPolicyPair(
                new EvaluationPolicyInfo(request.PolicyA.Id, request.PolicyA.Description),
                new EvaluationPolicyInfo(request.PolicyB.Id, request.PolicyB.Description)),
            new EvaluationSeatAssignment(
                request.Swapped ? "policyB" : "policyA",
                request.Swapped ? "policyA" : "policyB",
                request.Swapped),
            request.Schedule,
            days.OrderBy(pair => pair.Key).Select(pair => pair.Value.Build()).ToArray(),
            timings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.Build(pair.Key)).ToArray(),
            new EvaluationFinalMetrics(
                final.WinnerIds.ToArray(),
                final.Standings.Select(standing => new EvaluationFinalStanding(
                    standing.PlayerId,
                    policies[standing.PlayerId].Id,
                    standing.Rank,
                    standing.TotalTwigs,
                    standing.FrozenNightTenReward,
                    standing.DreamTwigs,
                    standing.IsWinner)).ToArray()),
            traces?.ToArray());
        return new EvaluationRunOutcome(result, DuckSaves.Capture(match));
    }

    private static IReadOnlyDictionary<string, IEvaluationPolicy> AssignPolicies(EvaluationMatchRequest request) =>
        new Dictionary<string, IEvaluationPolicy>(StringComparer.Ordinal)
        {
            ["human"] = request.Swapped ? request.PolicyB : request.PolicyA,
            ["ai"] = request.Swapped ? request.PolicyA : request.PolicyB
        };

    internal static IReadOnlyList<string> PlayerOrder(EvaluationSchedule schedule, int day) => schedule switch
    {
        EvaluationSchedule.Cli => PlayerIds,
        EvaluationSchedule.Reversed => new[] { "ai", "human" },
        EvaluationSchedule.AlternatingDay => day % 2 == 1 ? PlayerIds : new[] { "ai", "human" },
        _ => throw new ArgumentOutOfRangeException(nameof(schedule))
    };

    private static void CaptureDayStart(
        MatchSession<DuckMatchView> match,
        IReadOnlyDictionary<string, IEvaluationPolicy> policies,
        IDictionary<int, DayBuilder> days)
    {
        var publicView = match.GetSnapshot("human");
        if (publicView.Phase != DuckPhase.Adventure || days.ContainsKey(publicView.Day)) return;
        var day = new DayBuilder(publicView.Day, publicView.CurrentEvent.DefinitionId, publicView.NestLevel);
        foreach (var playerId in PlayerIds)
        {
            var ownView = match.GetSnapshot(playerId);
            var player = ownView.Players.Single(candidate => candidate.Id == playerId);
            day.Players[playerId] = new PlayerDayBuilder(
                playerId,
                policies[playerId].Id,
                new EvaluationDayStartMetrics(
                    player.EffectiveStart,
                    player.PermanentFeatherTrail,
                    player.ActiveMostRestedStep,
                    player.DawnTwigDeficit,
                    player.DawnFeathersAwarded,
                    Composition(ownView.OwnBag)),
                player.Position);
            if (publicView.Day > 1 && days.TryGetValue(publicView.Day - 1, out var prior))
            {
                prior.Players[playerId].NextDayStart = new EvaluationNextDayStartMetrics(
                    player.EffectiveStart,
                    player.PermanentFeatherTrail,
                    player.ActiveMostRestedStep,
                    player.DawnTwigDeficit,
                    player.DawnFeathersAwarded);
            }
        }
        days.Add(publicView.Day, day);
    }

    private static void UpdateAdventureProgress(
        DuckMatchView view,
        IReadOnlyDictionary<int, DayBuilder> days,
        IReadOnlyDictionary<string, EvaluationPolicyDecision> lastDecisions)
    {
        if (!days.TryGetValue(view.Day, out var day)) return;
        foreach (var player in view.Players)
        {
            var builder = day.Players[player.Id];
            builder.MaxSpace = Math.Max(builder.MaxSpace, player.Position);
            if (!player.HasFinishedDay || builder.FinishAction != null) continue;
            if (lastDecisions.TryGetValue(player.Id, out var decision))
            {
                builder.FinishAction = decision.Action.Kind;
                builder.FinishReason = decision.Reason;
            }
        }
    }

    private static void CaptureNightBoundary(
        DuckMatchView view,
        IReadOnlyDictionary<int, DayBuilder> days,
        IReadOnlyDictionary<string, EvaluationPolicyDecision> lastDecisions)
    {
        if (!days.TryGetValue(view.Day, out var day) || day.NightCaptured) return;
        if (view.Players.Any(player => player.LastNightOutcome?.Day != view.Day)) return;
        day.NightCaptured = true;
        foreach (var player in view.Players)
        {
            var builder = day.Players[player.Id];
            builder.MaxSpace = Math.Max(builder.MaxSpace, player.Position);
            if (builder.FinishAction == null && lastDecisions.TryGetValue(player.Id, out var decision))
            {
                builder.FinishAction = decision.Action.Kind;
                builder.FinishReason = decision.Reason;
            }
            var outcome = player.LastNightOutcome!;
            var space = view.Rules.BoardSpaceAt(player.Position);
            builder.Adventure = new EvaluationAdventureMetrics(
                player.PlacedChips.Count,
                builder.MaxSpace,
                player.Position,
                space.Biome,
                space.IsShelter,
                player.Position == view.Rules.BoardSpaces.Count,
                !player.IsWornOut,
                player.IsWornOut,
                player.Exhaustion,
                player.SafeExhaustionMaximum,
                builder.FinishAction ?? GameActionKind.Settle,
                builder.FinishReason ?? "The authoritative Night boundary completed the Day.");
            // Reeds and event Twigs are already banked during Adventure. Remove only
            // the Twig change applied by the Night boundary (plus final Dream Twigs).
            builder.PreNightTotalTwigs = player.TotalTwigs
                - (outcome.PrintedTwigs - outcome.BramblesPenalty + outcome.DreamTwigs);
            builder.EndOfNightTotalTwigs = player.TotalTwigs;
            builder.Night = Night(outcome);
            builder.SunshineChoice = player.HasGloriousSunshineChoice ? player.GloriousSunshineBenefit : null;
        }
    }

    private static void RecordPurchaseBeforeExecution(
        IReadOnlyDictionary<int, DayBuilder> days,
        DuckMatchView observation,
        string playerId,
        EvaluationPolicyDecision decision,
        long policyMicros)
    {
        if (decision.Action.Kind != GameActionKind.BuyEncounter) return;
        var offer = observation.ShopOffers.Single(candidate => candidate.DefinitionId == decision.Action.DefinitionId);
        days[observation.Day].Players[playerId].Purchases.Add(new EvaluationPurchaseMetrics(
            offer.DefinitionId, offer.ShopType, offer.Price, decision.Reason, policyMicros));
    }

    private static void RecordUnspentRewardBeforeFinish(
        IReadOnlyDictionary<int, DayBuilder> days,
        DuckMatchView observation,
        string playerId,
        GameAction action)
    {
        if (action.Kind != GameActionKind.FinishDream) return;
        var player = observation.Players.Single(candidate => candidate.Id == playerId);
        days[observation.Day].Players[playerId].UnspentReward = player.RemainingReward;
    }

    private static void CaptureDreamEnd(
        MatchSession<DuckMatchView> match,
        DuckMatchView view,
        IReadOnlyDictionary<int, DayBuilder> days)
    {
        if (!days.TryGetValue(view.Day, out var day)) return;
        foreach (var playerId in PlayerIds)
        {
            var own = match.GetSnapshot(playerId);
            var player = own.Players.Single(candidate => candidate.Id == playerId);
            if (!player.HasFinishedDream && view.Phase != DuckPhase.Finished) continue;
            var builder = day.Players[playerId];
            builder.UnspentReward ??= view.Phase == DuckPhase.Finished ? 0 : player.RemainingReward;
            builder.EndInventoryComposition = Composition(own.OwnInventory);
        }
    }

    internal static EvaluationNightMetrics Night(DuckNightOutcome outcome) => new(
        outcome.Day, outcome.PrintedReward, outcome.PrintedTwigs, outcome.ReedsTwigs, outcome.EventTwigs,
        outcome.BramblesPenalty, outcome.FlowerReward, outcome.FinalShelterReward,
        outcome.RestlessNightPenalty, outcome.CollectiveEventReward, outcome.GloriousSunshineReward, outcome.FlockReward,
        outcome.PebblesPenalty, outcome.RewardBeforeWear, outcome.FrozenReward,
        outcome.TotalTwigsEarned, outcome.FeathersAwarded, outcome.IsMostRested,
        outcome.NextDayTemporaryStep, outcome.DreamTwigs);

    private static IReadOnlyDictionary<string, int> Composition(IEnumerable<DuckPhysicalChipView> chips) =>
        new SortedDictionary<string, int>(chips.GroupBy(chip => chip.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), StringComparer.Ordinal);

    private static long Microseconds(long elapsedTicks) => elapsedTicks * 1_000_000L / Stopwatch.Frequency;

    private static TimingBuilder Timing(IDictionary<string, TimingBuilder> timings, string policyId)
    {
        if (!timings.TryGetValue(policyId, out var timing)) timings.Add(policyId, timing = new TimingBuilder());
        return timing;
    }

    private static void Validate(EvaluationMatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PolicyA);
        ArgumentNullException.ThrowIfNull(request.PolicyB);
        if (string.IsNullOrWhiteSpace(request.SourceLabel))
            throw new ArgumentException("A full caller-specified source label is required.", nameof(request));
        if (!Enum.IsDefined(request.Schedule)) throw new ArgumentOutOfRangeException(nameof(request));
    }

    private sealed class TimingBuilder
    {
        private int _count;
        private long _policyTotal;
        private long _policyMax;
        private long _executeTotal;
        private long _executeMax;
        public void Add(long policy, long execute)
        {
            _count++;
            _policyTotal += policy;
            _policyMax = Math.Max(_policyMax, policy);
            _executeTotal += execute;
            _executeMax = Math.Max(_executeMax, execute);
        }
        public EvaluationTimingSummary Build(string policyId) =>
            new(policyId, _count, _policyTotal, _policyMax, _executeTotal, _executeMax);
    }

    private sealed class DayBuilder
    {
        public DayBuilder(int day, string eventDefinitionId, int nestLevel)
        {
            Day = day;
            EventDefinitionId = eventDefinitionId;
            NestLevel = nestLevel;
        }
        public int Day { get; }
        public string EventDefinitionId { get; }
        public int NestLevel { get; }
        public Dictionary<string, PlayerDayBuilder> Players { get; } = new(StringComparer.Ordinal);
        public bool NightCaptured { get; set; }
        public EvaluationDayMetrics Build() => new(Day, EventDefinitionId, NestLevel,
            PlayerIds.Select(id => Players[id].Build()).ToArray());
    }

    private sealed class PlayerDayBuilder
    {
        public PlayerDayBuilder(string playerId, string policyId, EvaluationDayStartMetrics start, int maxSpace)
        {
            PlayerId = playerId;
            PolicyId = policyId;
            Start = start;
            MaxSpace = maxSpace;
        }
        public string PlayerId { get; }
        public string PolicyId { get; }
        public EvaluationDayStartMetrics Start { get; }
        public int MaxSpace { get; set; }
        public GameActionKind? FinishAction { get; set; }
        public string? FinishReason { get; set; }
        public EvaluationAdventureMetrics? Adventure { get; set; }
        public int PreNightTotalTwigs { get; set; }
        public int EndOfNightTotalTwigs { get; set; }
        public EvaluationNightMetrics? Night { get; set; }
        public List<EvaluationPurchaseMetrics> Purchases { get; } = new();
        public int? UnspentReward { get; set; }
        public IReadOnlyDictionary<string, int>? EndInventoryComposition { get; set; }
        public EvaluationNextDayStartMetrics? NextDayStart { get; set; }
        public DuckGloriousSunshineBenefit? SunshineChoice { get; set; }
        public EvaluationPlayerDayMetrics Build() => new(
            PlayerId, PolicyId, Start,
            Adventure ?? throw new InvalidOperationException($"Day metrics for {PlayerId} lack an Adventure boundary."),
            PreNightTotalTwigs, EndOfNightTotalTwigs,
            Night ?? throw new InvalidOperationException($"Day metrics for {PlayerId} lack a Night outcome."),
            Purchases.ToArray(), UnspentReward
                ?? throw new InvalidOperationException($"Day metrics for {PlayerId} lack unspent Reward."),
            EndInventoryComposition ?? throw new InvalidOperationException($"Day metrics for {PlayerId} lack inventory composition."),
            NextDayStart, SunshineChoice);
    }
}
