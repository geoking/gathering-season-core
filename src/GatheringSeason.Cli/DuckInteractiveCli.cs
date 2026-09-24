using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Cli;

internal sealed class DuckInteractiveCli
{
    private readonly DuckCliOptions _options;
    private readonly DuckSaveStore? _store;
    private readonly DuckNormalPolicy _normal = new();
    private MatchSession<DuckMatchView> _match;
    private int? _seed;
    private string _viewer = "human";

    internal DuckInteractiveCli(
        DuckCliOptions options,
        MatchSession<DuckMatchView> match,
        DuckSaveStore? store,
        int? seed)
    {
        _options = options;
        _match = match;
        _store = store;
        _seed = seed;
    }

    internal int Run()
    {
        Console.WriteLine(_options.TwoPlayer ? "Developer two-duck controls." : "Human versus Normal AI.");
        DuckCliRenderer.ShowHelp(_options.TwoPlayer);
        var advanceAi = !_options.TwoPlayer && (_seed.HasValue || ShouldAdvanceOpponentAfterResume());
        while (true)
        {
            if (advanceAi)
            {
                AdvanceOpponent();
                advanceAi = false;
            }

            var issued = IssueActions();
            if (issued.Values.All(actions => actions.Count == 0))
            {
                if (_match.GetSnapshot(_viewer).Phase != DuckPhase.Finished)
                    throw new InvalidOperationException("No active duck has a legal action before the game is finished.");
                Console.WriteLine("Game complete. Enter r to restart or q to quit.");
            }
            ShowActions(issued);
            Console.Write("> ");
            var raw = Console.ReadLine();
            if (raw == null) { Console.WriteLine("Input closed. Quitting."); return 0; }
            var input = raw.Trim();
            if (input.Equals("q", StringComparison.OrdinalIgnoreCase) || input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return 0;
            if (input.Equals("r", StringComparison.OrdinalIgnoreCase) || input.Equals("restart", StringComparison.OrdinalIgnoreCase))
            {
                _seed = _options.CreateSeed(_seed);
                _match = MatchSession.CreateDuck(_seed.Value);
                Save();
                Console.WriteLine("New game · seed " + _seed);
                DuckCliRenderer.ShowStatus(_match.GetSnapshot(_viewer));
                advanceAi = !_options.TwoPlayer;
                continue;
            }
            if (TryShowInformation(input, issued)) continue;

            if (!_options.TwoPlayer && int.TryParse(input, out _)) input = "human:" + input;
            var parts = input.Split(':', 2);
            if (parts.Length != 2 || !issued.TryGetValue(parts[0], out var actions)
                || !int.TryParse(parts[1], out var number) || number < 1 || number > actions.Count)
            {
                Console.WriteLine(_options.TwoPlayer
                    ? "Choose a listed player:action entry, or enter help."
                    : "Choose one of the listed action numbers, or enter help.");
                continue;
            }

            var before = _match.GetSnapshot(_viewer);
            _match.Execute(parts[0], actions[number - 1]);
            Save();
            var after = _match.GetSnapshot(_viewer);
            DuckCliRenderer.ShowStatus(after);
            DuckCliRenderer.ShowNewNight(before, after);
            advanceAi = !_options.TwoPlayer;
        }
    }

    private Dictionary<string, IReadOnlyList<GameAction>> IssueActions()
    {
        var issued = new Dictionary<string, IReadOnlyList<GameAction>>(StringComparer.Ordinal);
        foreach (var id in _options.TwoPlayer ? new[] { "human", "ai" } : new[] { "human" })
            issued[id] = _match.GetLegalActions(id);
        return issued;
    }

    private void ShowActions(IReadOnlyDictionary<string, IReadOnlyList<GameAction>> issued)
    {
        foreach (var pair in issued)
            for (var index = 0; index < pair.Value.Count; index++)
                Console.WriteLine(_options.TwoPlayer
                    ? $"{pair.Key}:{index + 1}. {pair.Value[index].Label}"
                    : $"{index + 1}. {pair.Value[index].Label}");
    }

    private bool TryShowInformation(
        string input,
        IReadOnlyDictionary<string, IReadOnlyList<GameAction>> issued)
    {
        var view = _match.GetSnapshot(_viewer);
        var command = input.ToLowerInvariant();
        if (command is "help" or "h" or "?") DuckCliRenderer.ShowHelp(_options.TwoPlayer);
        else if (command is "status" or "view:human" || _options.TwoPlayer && command == "view:ai")
        {
            if (command.StartsWith("view:", StringComparison.Ordinal)) _viewer = command.Substring("view:".Length);
            DuckCliRenderer.ShowStatus(_match.GetSnapshot(_viewer));
        }
        else if (command == "view:ai") Console.WriteLine("The AI's private view is unavailable outside --two-player developer mode.");
        else if (command == "board") DuckCliRenderer.ShowBoard(view, null);
        else if (command.StartsWith("board ", StringComparison.Ordinal))
        {
            var value = command.Substring("board ".Length).Trim();
            DuckCliRenderer.ShowBoard(view, int.TryParse(value, out var space) ? space : -1);
        }
        else if (command is "pouch" or "bag") DuckCliRenderer.ShowBag(view);
        else if (command is "tokens" or "wishes") DuckCliRenderer.ShowTokens(view);
        else if (command == "shop") DuckCliRenderer.ShowShop(view, issued.TryGetValue(_viewer, out var actions) ? actions : _match.GetLegalActions(_viewer));
        else if (command == "event") DuckCliRenderer.ShowEvent(view);
        else if (command == "night") DuckCliRenderer.ShowNight(view);
        else if (command == "history") DuckCliRenderer.ShowHistory(view);
        else return false;
        return true;
    }

    private void AdvanceOpponent()
    {
        for (var step = 0; step < 256; step++)
        {
            var actions = _match.GetLegalActions("ai");
            if (actions.Count == 0 || actions.All(action => action.Kind == GameActionKind.NextDay)) return;
            var before = _match.GetSnapshot("ai");
            var action = _normal.Choose(before, actions);
            _match.Execute("ai", action);
            Save();
            var after = _match.GetSnapshot("human");
            Console.WriteLine(before.Day == 10 && before.Phase == DuckPhase.Adventure && after.AwaitingFinalDayDecisions
                ? "AI committed its hidden final-Day decision."
                : "AI: " + action.Label);
            DuckCliRenderer.ShowStatus(after);
            DuckCliRenderer.ShowNewNight(before, after);
            if (_match.GetLegalActions("human").Count > 0 || after.Phase == DuckPhase.Finished) return;
        }
        throw new InvalidOperationException("Opponent exceeded the action safety limit.");
    }

    private bool ShouldAdvanceOpponentAfterResume()
    {
        var humanView = _match.GetSnapshot("human");
        if (humanView.Phase == DuckPhase.Finished || _match.GetLegalActions("human").Count > 0)
            return false;

        var aiActions = _match.GetLegalActions("ai");
        return aiActions.Any(action => action.Kind != GameActionKind.NextDay);
    }

    private void Save()
    {
        if (_options.ShouldSave) _store!.Save(_match);
    }
}
