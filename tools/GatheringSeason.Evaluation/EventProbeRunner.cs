using System.Security.Cryptography;
using System.Text.Json;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Evaluation;

/// <summary>
/// Branches one legitimate completed Night into each still-unseen event at the next
/// Dawn. Core performs all preparation and rules; policies receive observations only.
/// These are counterfactual one-Day probes, not additional independent full games.
/// </summary>
public static class EventProbeRunner
{
    public static IEnumerable<EventProbeResult> Run(DuckSaveData source, EvaluationMatchRequest request)
    {
        if (source.Phase != DuckPhase.DayComplete || source.Day >= 10)
            throw new ArgumentException("Event probes require a completed shopping Night before Day 10.", nameof(source));
        var json = JsonSerializer.Serialize(source, new JsonSerializerOptions { IncludeFields = true });
        var stateHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        var nextIndex = source.CurrentEventIndex + 1;
        foreach (var eventId in source.WorldEventDeckDefinitionIds.Skip(nextIndex).Order(StringComparer.Ordinal))
        {
            var variants = eventId == "glorious_sunshine"
                ? new[] { "fresh-fresh", "fresh-warm", "warm-fresh", "warm-warm" }
                : new[] { "policy" };
            foreach (var variant in variants)
            {
                var save = DuckSaves.Capture(DuckSaves.Restore(source));
                var eventIndex = save.WorldEventDeckDefinitionIds.IndexOf(eventId);
                (save.WorldEventDeckDefinitionIds[nextIndex], save.WorldEventDeckDefinitionIds[eventIndex]) =
                    (save.WorldEventDeckDefinitionIds[eventIndex], save.WorldEventDeckDefinitionIds[nextIndex]);
                var match = DuckSaves.Restore(save);
                var dawn = match.GetLegalActions("human").Single(action => action.Kind == GameActionKind.NextDay);
                match.Execute("human", dawn);
                var start = match.GetSnapshot("human");
                var policyByPlayer = new Dictionary<string, Policies.IEvaluationPolicy>
                {
                    ["human"] = request.Swapped ? request.PolicyB : request.PolicyA,
                    ["ai"] = request.Swapped ? request.PolicyA : request.PolicyB
                };
                for (var step = 0; match.GetSnapshot("human").Phase == DuckPhase.Adventure; step++)
                {
                    if (step >= 256) throw new InvalidOperationException("Event probe failed to finish its Day.");
                    var acted = false;
                    foreach (var id in EvaluationRunner.PlayerOrder(request.Schedule, start.Day))
                    {
                        var actions = match.GetLegalActions(id);
                        if (actions.Count == 0) continue;
                        var view = match.GetSnapshot(id);
                        GameAction? action = null;
                        if (variant != "policy" && actions[0].Kind == GameActionKind.ChooseEventBenefit)
                        {
                            var choice = variant.Split('-')[id == "human" ? 0 : 1];
                            action = actions.Single(candidate => candidate.Id.EndsWith(
                                choice == "fresh" ? ".fresh-air" : ".warm-dreams", StringComparison.Ordinal));
                        }
                        action ??= policyByPlayer[id].Decide(view, actions).Action;
                        if (!actions.Any(candidate => ReferenceEquals(candidate, action)))
                            throw new InvalidOperationException("Probe policy returned an unissued action.");
                        match.Execute(id, action);
                        acted = true;
                        if (match.GetSnapshot("human").Phase != DuckPhase.Adventure) break;
                    }
                    if (!acted) throw new InvalidOperationException("Event probe stalled.");
                }
                var result = match.GetSnapshot("human");
                yield return new EventProbeResult(request.SourceLabel, request.Seed, request.Swapped,
                    request.Schedule, stateHash, result.RulesRevision, result.Economy.CurrencyName,
                    start.Day, eventId, variant, result.Players.Select(player =>
                    {
                        var initial = start.Players.Single(candidate => candidate.Id == player.Id);
                        return new EventProbePlayer(player.Id, policyByPlayer[player.Id].Id,
                            initial.EffectiveStart, initial.TotalTwigs, player.Position,
                            player.PlacedChips.Count, player.IsWornOut,
                            result.Rules.BoardSpaceAt(player.Position).IsShelter,
                            EvaluationRunner.Night(player.LastNightOutcome!));
                    }).ToArray());
            }
        }
    }
}

public sealed record EventProbeResult(string SourceLabel, int Seed, bool Swapped,
    EvaluationSchedule Schedule, string SourceStateHash, int RulesVersion, string Currency,
    int Day, string EventDefinitionId, string Variant, IReadOnlyList<EventProbePlayer> Players);
public sealed record EventProbePlayer(string PlayerId, string PolicyId, int StartSpace,
    int StartingTwigs, int RestSpace, int DrawCount, bool IsWornOut, bool IsShelter,
    EvaluationNightMetrics Night);
