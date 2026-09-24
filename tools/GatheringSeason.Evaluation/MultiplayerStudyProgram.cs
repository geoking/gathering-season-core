using System.Text.Json;

namespace GatheringSeason.Evaluation;

public sealed record MultiplayerStudyOptions(
    int SeedStart, int SeedCount, IReadOnlyList<int> PlayerCounts,
    string SourceLabel, string OutputPath, int ProgressEvery)
{
    public const string Help = """
        Gathering Season multiplayer study (separate from historical pairwise evaluation)
        --multiplayer --source-label LABEL [--seed-start N] [--seed-count N]
        [--players all|2|3|4] [--output JSONL_PATH|-] [--progress-every N]
        Per seed and player count: one all-Normal baseline; Movement and Reeds
        each rotated through every seat against Normal; balanced mixed lobbies.
        """;

    public static MultiplayerStudyOptions Parse(IReadOnlyList<string> args)
    {
        var seedStart = 0;
        var seedCount = 1;
        IReadOnlyList<int> playerCounts = new[] { 2, 3, 4 };
        string? sourceLabel = null;
        var output = "-";
        var progressEvery = 10;
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            string Value()
            {
                if (++index >= args.Count) throw new ArgumentException(option + " requires a value.");
                return args[index];
            }
            switch (option)
            {
                case "--seed-start": seedStart = Integer(Value(), option); break;
                case "--seed-count": seedCount = Integer(Value(), option); break;
                case "--players":
                    var players = Value();
                    playerCounts = players == "all" ? new[] { 2, 3, 4 }
                        : int.TryParse(players, out var count) && count is >= 2 and <= 4
                            ? new[] { count }
                            : throw new ArgumentException("--players requires all, 2, 3 or 4.");
                    break;
                case "--source-label": sourceLabel = Value(); break;
                case "--output": output = Value(); break;
                case "--progress-every": progressEvery = Integer(Value(), option); break;
                default: throw new ArgumentException("Unknown multiplayer option: " + option);
            }
        }
        if (seedCount <= 0) throw new ArgumentOutOfRangeException(nameof(seedCount));
        if (progressEvery < 0) throw new ArgumentOutOfRangeException(nameof(progressEvery));
        if (string.IsNullOrWhiteSpace(sourceLabel))
            throw new ArgumentException("--source-label is required for reproducible evidence.");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output requires a path or -.");
        _ = checked(seedStart + seedCount);
        return new MultiplayerStudyOptions(seedStart, seedCount, playerCounts, sourceLabel, output, progressEvery);
    }

    private static int Integer(string value, string option) => int.TryParse(value, out var parsed)
        ? parsed : throw new ArgumentException(option + " requires an integer.");
}

public static class MultiplayerStudyProgram
{
    public static int Run(string[] args)
    {
        if (args.Any(arg => arg is "--help" or "-h"))
        {
            Console.WriteLine(MultiplayerStudyOptions.Help);
            return 0;
        }
        var options = MultiplayerStudyOptions.Parse(args);
        var runner = new MultiplayerStudyRunner();
        TextWriter output = options.OutputPath == "-" ? Console.Out : CreateOutput(options.OutputPath);
        using var owned = ReferenceEquals(output, Console.Out) ? null : output;
        var total = options.SeedCount * options.PlayerCounts.Sum(count => MultiplayerStudyRunner.Assignments(count).Count);
        var completed = 0;
        for (var seed = options.SeedStart; seed < checked(options.SeedStart + options.SeedCount); seed++)
            foreach (var playerCount in options.PlayerCounts)
                foreach (var assignment in MultiplayerStudyRunner.Assignments(playerCount))
                {
                    var result = runner.Run(seed, playerCount, assignment, options.SourceLabel);
                    output.WriteLine(JsonSerializer.Serialize(result, EvaluationProgram.Json));
                    completed++;
                    if (options.ProgressEvery > 0 && (completed % options.ProgressEvery == 0 || completed == total))
                        Console.Error.WriteLine($"evaluated {completed}/{total} multiplayer matches");
                }
        output.Flush();
        return 0;
    }

    private static StreamWriter CreateOutput(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new StreamWriter(fullPath, append: false);
    }
}
