using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using GatheringSeason.Evaluation.Policies;

namespace GatheringSeason.Evaluation;

public sealed record MultiplayerStudyAssignment(string Scenario, IReadOnlyDictionary<string, string> Styles);

public sealed record MultiplayerStudyPlayer(
    string PlayerId, string Style, int Rank, int TotalTwigs, int FinalStars,
    bool Win, bool SharedWin, int FinalLeaderDeficit, int MaximumLeaderDeficit,
    int ActionCount, int DrawActions, int SettleActions, int PurchaseActions, int WearOutDays,
    int OasisSafeDays, int OasisWornDays, long PolicyMicroseconds, long ExecuteMicroseconds);

public sealed record MultiplayerStudyDayPlayer(
    string PlayerId, int StartTwigs, int DawnLeaderDeficit, int EndTwigs, int EndLeaderDeficit,
    int Draws, int RestSpace, bool WornOut, bool OasisReached, bool OasisSafe,
    bool OasisWorn, int ReedsTwigs, int FrozenStars, IReadOnlyList<string> Purchases);

public sealed record MultiplayerStudyDay(
    int Day, string EventId, bool CollectiveAttempt, bool CollectiveTriggered,
    IReadOnlyList<MultiplayerStudyDayPlayer> Players);

public sealed record MultiplayerStudyResult(
    int SchemaVersion, string SourceLabel, string CoreAssemblyVersion, int RulesRevision,
    int Seed, int PlayerCount, string Scenario, IReadOnlyDictionary<string, string> Styles,
    IReadOnlyList<MultiplayerStudyPlayer> Players, IReadOnlyList<MultiplayerStudyDay> Days,
    IReadOnlyList<string> WinnerIds, int WinningScoreGap, int ActionCount, long DurationMicroseconds);

/// <summary>Separate multiplayer telemetry. Historical two-seat evaluation remains unchanged.</summary>
public sealed class MultiplayerStudyRunner
{
    private const int MaximumActions = 8000;

    public static IReadOnlyList<MultiplayerStudyAssignment> Assignments(int playerCount)
    {
        var ids = SeatIds(playerCount);
        var assignments = new List<MultiplayerStudyAssignment>();
        var normal = ids.ToDictionary(id => id, _ => "normal", StringComparer.Ordinal);
        assignments.Add(new MultiplayerStudyAssignment("all-normal", normal));
        foreach (var style in new[] { "movement-heavy", "reeds-heavy" })
            foreach (var focal in ids)
            {
                var styles = new Dictionary<string, string>(normal, StringComparer.Ordinal) { [focal] = style };
                assignments.Add(new MultiplayerStudyAssignment($"focal-{style}-{focal}", styles));
            }
        var mixed = new HashSet<string>(StringComparer.Ordinal);
        for (var movement = 0; movement < ids.Length; movement++)
            foreach (var offset in new[] { 1, ids.Length - 1 })
            {
                var reeds = (movement + offset) % ids.Length;
                var key = ids[movement] + ":" + ids[reeds];
                if (!mixed.Add(key)) continue;
                var styles = new Dictionary<string, string>(normal, StringComparer.Ordinal)
                {
                    [ids[movement]] = "movement-heavy",
                    [ids[reeds]] = "reeds-heavy"
                };
                assignments.Add(new MultiplayerStudyAssignment($"mixed-m-{ids[movement]}-r-{ids[reeds]}", styles));
            }
        return assignments;
    }

    public MultiplayerStudyResult Run(int seed, int playerCount, MultiplayerStudyAssignment assignment, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel)) throw new ArgumentException("A source label is required.", nameof(sourceLabel));
        ArgumentNullException.ThrowIfNull(assignment);
        var ids = SeatIds(playerCount);
        if (assignment.Styles.Count != ids.Length || ids.Any(id => !assignment.Styles.ContainsKey(id)))
            throw new ArgumentException("Assignment must specify every configured seat once.", nameof(assignment));
        var policies = ids.ToDictionary(id => id, id => EvaluationPolicies.Create(assignment.Styles[id]), StringComparer.Ordinal);
        if (policies.Values.Any(policy => policy.Id is not ("normal" or "movement-heavy" or "reeds-heavy")))
            throw new ArgumentException("Multiplayer styles must share Normal adventure choices.", nameof(assignment));

        var match = MatchSession.CreateDuck(seed, new DuckMatchSettings(playerCount));
        var days = new SortedDictionary<int, DayAccumulator>();
        var players = ids.ToDictionary(id => id, _ => new PlayerAccumulator(), StringComparer.Ordinal);
        var started = Stopwatch.GetTimestamp();
        var actionCount = 0;
        while (match.GetSnapshot("human").Phase != DuckPhase.Finished)
        {
            if (actionCount >= MaximumActions)
                throw new InvalidOperationException("Multiplayer study exceeded the action safety bound.");
            CaptureDayStart(match, ids, days);
            var pass = match.GetSnapshot("human");
            var frozenBeat = pass.Day == 10 && pass.Phase == DuckPhase.Adventure
                && ids.Any(id => match.GetLegalActions(id).Any(action => action.Kind is GameActionKind.Explore or GameActionKind.Settle));
            var acted = false;
            if (frozenBeat)
            {
                // Policies see the same public state and only their own private view.
                var cohort = ids.Select(id => Decide(match, id, policies[id])).Where(choice => choice != null).ToArray();
                foreach (var choice in cohort)
                    acted |= Execute(choice!.Value);
            }
            else
            {
                foreach (var id in ids)
                {
                    var choice = Decide(match, id, policies[id]);
                    if (choice == null) continue;
                    acted |= Execute(choice.Value);
                    if (match.GetSnapshot("human").Day != pass.Day) break;
                }
            }
            if (!acted) throw new InvalidOperationException("Multiplayer study stalled without an issued action.");
        }

        var final = match.GetSnapshot("human");
        CaptureNight(final, days);
        var standings = final.FinalResult?.Standings ?? throw new InvalidOperationException("Finished match lacks standings.");
        var leader = standings.Max(standing => standing.TotalTwigs);
        var second = standings.Select(standing => standing.TotalTwigs).OrderDescending().Skip(1).First();
        var resultPlayers = standings.Select(standing =>
        {
            var id = standing.PlayerId;
            var observed = players[id];
            var maxDeficit = days.Values.Max(day => day.Players[id].EndLeaderDeficit);
            return new MultiplayerStudyPlayer(id, assignment.Styles[id], standing.Rank, standing.TotalTwigs,
                standing.FrozenNightTenReward, standing.IsWinner, standing.IsWinner && final.FinalResult.WinnerIds.Count > 1,
                leader - standing.TotalTwigs, maxDeficit, observed.ActionCount, observed.DrawActions, observed.SettleActions,
                observed.PurchaseActions, days.Values.Count(day => day.Players[id].WornOut),
                days.Values.Count(day => day.Players[id].OasisSafe),
                days.Values.Count(day => day.Players[id].OasisWorn),
                observed.PolicyMicroseconds, observed.ExecuteMicroseconds);
        }).OrderBy(player => Array.IndexOf(ids, player.PlayerId)).ToArray();
        return new MultiplayerStudyResult(1, sourceLabel,
            typeof(MatchSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MatchSession).Assembly.GetName().Version?.ToString() ?? "unknown",
            final.RulesRevision, seed, playerCount, assignment.Scenario,
            new Dictionary<string, string>(assignment.Styles, StringComparer.Ordinal), resultPlayers,
            days.Values.Select(day => day.Build(ids)).ToArray(), final.FinalResult.WinnerIds.ToArray(),
            leader - second, actionCount, Microseconds(Stopwatch.GetTimestamp() - started));

        bool Execute(Choice choice)
        {
            var legalNow = match.GetLegalActions(choice.PlayerId);
            if (!legalNow.Any(action => action.Id == choice.Action.Id))
                throw new InvalidOperationException($"Issued action became stale for {choice.PlayerId}.");
            var before = match.GetSnapshot(choice.PlayerId);
            if (choice.Action.Kind == GameActionKind.BuyEncounter)
                days[before.Day].Players[choice.PlayerId].Purchases.Add(choice.Action.DefinitionId);
            var timer = Stopwatch.GetTimestamp();
            match.Execute(choice.PlayerId, choice.Action);
            var elapsed = Microseconds(Stopwatch.GetTimestamp() - timer);
            var counter = players[choice.PlayerId];
            counter.PolicyMicroseconds += choice.PolicyMicroseconds;
            counter.ExecuteMicroseconds += elapsed;
            counter.ActionCount++;
            if (choice.Action.Kind == GameActionKind.Explore) counter.DrawActions++;
            if (choice.Action.Kind == GameActionKind.Settle) counter.SettleActions++;
            if (choice.Action.Kind == GameActionKind.BuyEncounter) counter.PurchaseActions++;
            actionCount++;
            CaptureNight(match.GetSnapshot("human"), days);
            return true;
        }
    }

    private static Choice? Decide(MatchSession<DuckMatchView> match, string id, IEvaluationPolicy policy)
    {
        var actions = match.GetLegalActions(id);
        if (actions.Count == 0) return null;
        var view = match.GetSnapshot(id);
        var started = Stopwatch.GetTimestamp();
        var decision = policy.Decide(view, actions);
        var elapsed = Microseconds(Stopwatch.GetTimestamp() - started);
        if (decision?.Action == null || !actions.Any(action => ReferenceEquals(action, decision.Action)))
            throw new InvalidOperationException($"Policy {policy.Id} must choose an issued action for {id}.");
        return new Choice(id, decision.Action, elapsed);
    }

    private static void CaptureDayStart(MatchSession<DuckMatchView> match, IReadOnlyList<string> ids,
        IDictionary<int, DayAccumulator> days)
    {
        var view = match.GetSnapshot("human");
        if (view.Phase != DuckPhase.Adventure || days.ContainsKey(view.Day)) return;
        var day = new DayAccumulator(view.Day, view.CurrentEvent.DefinitionId, view.CurrentEvent.EventType);
        foreach (var player in view.Players)
            day.Players.Add(player.Id, new DayPlayerAccumulator(player.TotalTwigs, player.DawnTwigDeficit));
        days.Add(view.Day, day);
    }

    private static void CaptureNight(DuckMatchView view, IDictionary<int, DayAccumulator> days)
    {
        if (!days.TryGetValue(view.Day, out var day) || day.Completed) return;
        if (view.Players.Any(player => player.LastNightOutcome?.Day != view.Day)) return;
        day.Completed = true;
        var leader = view.Players.Max(player => player.TotalTwigs);
        foreach (var player in view.Players)
        {
            var metric = day.Players[player.Id];
            metric.EndTwigs = player.TotalTwigs;
            metric.EndLeaderDeficit = leader - player.TotalTwigs;
            metric.Draws = player.PlacedChips.Count;
            metric.RestSpace = player.Position;
            metric.WornOut = player.IsWornOut;
            metric.OasisReached = player.Position == view.Rules.BoardSpaces.Count;
            metric.OasisSafe = metric.OasisReached && !player.IsWornOut;
            metric.OasisWorn = metric.OasisReached && player.IsWornOut;
            metric.ReedsTwigs = player.LastNightOutcome!.ReedsTwigs;
            metric.FrozenStars = player.LastNightOutcome.FrozenReward;
        }
        day.CollectiveTriggered = day.CollectiveAttempt
            && view.Players.All(player => player.LastNightOutcome!.CollectiveEventReward > 0);
    }

    private static string[] SeatIds(int count) => count switch
    {
        2 => new[] { "human", "ai" },
        3 => new[] { "human", "ai", "ai-2" },
        4 => new[] { "human", "ai", "ai-2", "ai-3" },
        _ => throw new ArgumentOutOfRangeException(nameof(count), "Use 2, 3 or 4 players.")
    };

    private static long Microseconds(long ticks) => ticks * 1_000_000L / Stopwatch.Frequency;

    private readonly record struct Choice(string PlayerId, GameAction Action, long PolicyMicroseconds);

    private sealed class PlayerAccumulator
    {
        internal int ActionCount, DrawActions, SettleActions, PurchaseActions;
        internal long PolicyMicroseconds, ExecuteMicroseconds;
    }

    private sealed class DayAccumulator
    {
        internal DayAccumulator(int day, string eventId, DuckWorldEventType eventType)
        {
            Day = day;
            EventId = eventId;
            CollectiveAttempt = eventType is DuckWorldEventType.AllTuckedIn
                or DuckWorldEventType.HomeBeforeDark or DuckWorldEventType.SharedSupper;
        }
        internal int Day { get; }
        internal string EventId { get; }
        internal bool CollectiveAttempt { get; }
        internal bool CollectiveTriggered { get; set; }
        internal bool Completed { get; set; }
        internal Dictionary<string, DayPlayerAccumulator> Players { get; } = new(StringComparer.Ordinal);
        internal MultiplayerStudyDay Build(IReadOnlyList<string> ids)
        {
            if (!Completed) throw new InvalidOperationException($"Day {Day} has no Night outcome.");
            return new MultiplayerStudyDay(Day, EventId, CollectiveAttempt, CollectiveTriggered,
                ids.Select(id => Players[id].Build(id)).ToArray());
        }
    }

    private sealed class DayPlayerAccumulator
    {
        internal DayPlayerAccumulator(int startTwigs, int dawnDeficit)
        {
            StartTwigs = startTwigs;
            DawnDeficit = dawnDeficit;
        }
        internal int StartTwigs { get; }
        internal int DawnDeficit { get; }
        internal int EndTwigs, EndLeaderDeficit, Draws, RestSpace, ReedsTwigs, FrozenStars;
        internal bool WornOut, OasisReached, OasisSafe, OasisWorn;
        internal List<string> Purchases { get; } = new();
        internal MultiplayerStudyDayPlayer Build(string id) => new(id, StartTwigs, DawnDeficit,
            EndTwigs, EndLeaderDeficit, Draws, RestSpace, WornOut, OasisReached,
            OasisSafe, OasisWorn, ReedsTwigs, FrozenStars, Purchases.ToArray());
    }
}
