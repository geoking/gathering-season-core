using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Cli;

internal static class DuckCliDemos
{
    internal static int RunDay(MatchSession<DuckMatchView> match, Action saveAfterAction)
    {
        Console.WriteLine("Deterministic daily-cycle smoke demonstration; these are scripted choices, not Normal AI.");
        for (var step = 0; step < 200; step++)
        {
            var view = match.GetSnapshot("human");
            if (view.Day == 2)
            {
                Console.WriteLine("Verified CLI boundary reached: Day 1 → Night 1 → Day 2.");
                return 0;
            }
            var acted = false;
            foreach (var id in new[] { "human", "ai" })
            {
                view = match.GetSnapshot(id);
                var player = view.Players.Single(candidate => candidate.Id == id);
                var actions = match.GetLegalActions(id);
                var chosen = actions.FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit)
                    ?? actions.FirstOrDefault(action => action.Kind == GameActionKind.NextDay)
                    ?? actions.FirstOrDefault(action => action.Kind == GameActionKind.Settle && player.PlacedChips.Count >= (id == "human" ? 4 : 3))
                    ?? actions.FirstOrDefault(action => action.Kind == GameActionKind.Explore)
                    ?? actions.FirstOrDefault(action => action.Kind == GameActionKind.BuyEncounter)
                    ?? actions.FirstOrDefault(action => action.Kind == GameActionKind.FinishDream);
                if (chosen == null) continue;
                var before = match.GetSnapshot("human");
                Console.WriteLine($"{id}: {chosen.Label}");
                match.Execute(id, chosen);
                saveAfterAction();
                var after = match.GetSnapshot("human");
                DuckCliRenderer.ShowStatus(after);
                DuckCliRenderer.ShowNewNight(before, after);
                acted = true;
                if (after.Day == 2) break;
            }
            if (!acted) throw new InvalidOperationException("The daily-cycle demo has no legal action; the current checkpoint is incomplete.");
        }
        throw new InvalidOperationException("The daily-cycle demonstration exceeded its action bound.");
    }

    internal static int RunGame(MatchSession<DuckMatchView> match, Action saveAfterAction)
    {
        var normal = new DuckNormalPolicy();
        Console.WriteLine("Ten-Day demonstration: Normal policy controls both ducks using their own observations.");
        for (var step = 0; step < 4000; step++)
        {
            if (match.GetSnapshot("human").Phase == DuckPhase.Finished)
            {
                Console.WriteLine("Complete ten-Day match finished.");
                return 0;
            }
            var acted = false;
            foreach (var id in new[] { "human", "ai" })
            {
                var actions = match.GetLegalActions(id);
                if (actions.Count == 0) continue;
                var before = match.GetSnapshot(id);
                var chosen = normal.Choose(before, actions);
                match.Execute(id, chosen);
                saveAfterAction();
                var after = match.GetSnapshot("human");
                Console.WriteLine(before.Day == 10 && before.Phase == DuckPhase.Adventure && after.AwaitingFinalDayDecisions
                    ? id + " committed a hidden final-Day decision."
                    : id + ": " + chosen.Label);
                DuckCliRenderer.ShowStatus(after);
                DuckCliRenderer.ShowNewNight(before, after);
                acted = true;
            }
            if (!acted) throw new InvalidOperationException("The match has no legal action before final scoring.");
        }
        throw new InvalidOperationException("The ten-Day demonstration exceeded its action bound.");
    }
}
