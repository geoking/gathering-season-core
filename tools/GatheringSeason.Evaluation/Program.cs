using System.Text.Json;
using System.Text.Json.Serialization;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Evaluation;
using GatheringSeason.Evaluation.Policies;

return EvaluationProgram.Run(args);

public static class EvaluationProgram
{
    internal static readonly JsonSerializerOptions Json = CreateJson(indented: false);
    internal static readonly JsonSerializerOptions PrettyJson = CreateJson(indented: true);

    public static int Run(string[] args)
    {
        try
        {
            if (args.Contains("--multiplayer", StringComparer.Ordinal))
                return MultiplayerStudyProgram.Run(args.Where(arg => arg != "--multiplayer").ToArray());
            var options = EvaluationOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(EvaluationOptions.Help);
                return 0;
            }
            Run(options);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine("Evaluation failed: " + exception.Message);
            return 2;
        }
    }

    public static void Run(EvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateArtifactPaths(options);
        var runner = new EvaluationRunner();
        var seats = options.SeatMode switch
        {
            EvaluationSeatMode.Original => new[] { false },
            EvaluationSeatMode.Swapped => new[] { true },
            EvaluationSeatMode.Both => new[] { false, true },
            _ => throw new ArgumentOutOfRangeException(nameof(options.SeatMode))
        };
        var representativeSeed = options.RepresentativeSeed ?? options.SeedStart;
        var seedEndExclusive = checked(options.SeedStart + options.SeedCount);
        if (representativeSeed < options.SeedStart || representativeSeed >= seedEndExclusive)
            throw new ArgumentException("Representative seed must be inside the requested seed range.");

        TextWriter output = options.OutputPath == "-"
            ? Console.Out
            : CreateWriter(options.OutputPath);
        using var ownedOutput = ReferenceEquals(output, Console.Out) ? null : output;
        using var probes = options.EventProbesPath == null ? null : CreateWriter(options.EventProbesPath);
        var matchIndex = 0;
        var total = checked(options.SeedCount * seats.Length);
        var representativeWritten = false;
        for (var offset = 0; offset < options.SeedCount; offset++)
        {
            var seed = checked(options.SeedStart + offset);
            foreach (var swapped in seats)
            {
                var isRepresentative = seed == representativeSeed
                    && swapped == (options.RepresentativeSeat == EvaluationSeatMode.Swapped);
                var includeActions = options.IncludeActions || isRepresentative && options.TracePath != null;
                var request = new EvaluationMatchRequest(
                    seed,
                    matchIndex,
                    EvaluationPolicies.Create(options.PolicyA),
                    EvaluationPolicies.Create(options.PolicyB),
                    swapped,
                    options.Schedule,
                    options.SourceLabel,
                    includeActions);
                var outcome = runner.Run(request, probes == null ? null : save =>
                {
                    foreach (var probe in EventProbeRunner.Run(save, request))
                        probes.WriteLine(JsonSerializer.Serialize(probe, Json));
                });
                var jsonlResult = options.IncludeActions ? outcome.Result : outcome.Result with { Actions = null };
                output.WriteLine(JsonSerializer.Serialize(jsonlResult, Json));
                matchIndex++;

                if (isRepresentative && !representativeWritten)
                {
                    if (options.TracePath != null) WriteJson(options.TracePath, outcome.Result, PrettyJson);
                    if (options.SavePath != null) WriteJson(options.SavePath, outcome.FinalSave, SaveJson());
                    representativeWritten = true;
                }
                if (options.ProgressEvery > 0 && (matchIndex % options.ProgressEvery == 0 || matchIndex == total))
                    Console.Error.WriteLine($"evaluated {matchIndex}/{total} matches");
            }
        }
        output.Flush();
    }

    private static StreamWriter CreateWriter(string path)
    {
        EnsureParent(path);
        return new StreamWriter(path, append: false);
    }

    private static void WriteJson<T>(string path, T value, JsonSerializerOptions json)
    {
        EnsureParent(path);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, value, json);
        stream.WriteByte((byte)'\n');
    }

    private static void EnsureParent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "-")
            throw new ArgumentException("Artifact paths must be non-empty filesystem paths.");
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (parent != null) Directory.CreateDirectory(parent);
    }

    private static void ValidateArtifactPaths(EvaluationOptions options)
    {
        var paths = new[] { options.OutputPath == "-" ? null : options.OutputPath, options.TracePath, options.SavePath, options.EventProbesPath }
            .Where(path => path != null)
            .Select(path => Path.GetFullPath(path!))
            .ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new ArgumentException("Output, trace, and save paths must be distinct.");
    }

    private static JsonSerializerOptions CreateJson(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static JsonSerializerOptions SaveJson()
    {
        var options = CreateJson(indented: true);
        options.IncludeFields = true;
        return options;
    }
}

public enum EvaluationSeatMode
{
    Original,
    Swapped,
    Both
}

public sealed record EvaluationOptions(
    int SeedStart,
    int SeedCount,
    string PolicyA,
    string PolicyB,
    EvaluationSeatMode SeatMode,
    EvaluationSchedule Schedule,
    string OutputPath,
    string? TracePath,
    string? SavePath,
    int? RepresentativeSeed,
    EvaluationSeatMode RepresentativeSeat,
    string SourceLabel,
    int ProgressEvery,
    bool IncludeActions,
    bool ShowHelp)
{
    public string? EventProbesPath { get; init; }

    public const string Help = """
        Gathering Season reproducible Duck evaluation runner

        Use --multiplayer for separate 2/3/4-player rotated-seat studies.

        Required:
          --source-label LABEL          Full source/build label recorded in every match

        Match set:
          --seed-start N                First seed (default: 0)
          --seed-count N                Number of seeds (default: 1)
          --policy-a ID                 baseline|normal|cautious|adventurous|movement-heavy|reeds-heavy|sandbag-day3
                                        flowers-first|seeds-first|sunshine-fresh|sunshine-warm
          --policy-b ID                 Same IDs (default: normal)
          --seat-mode MODE              original|swapped|both (default: original)
          --swapped-seats               Alias for --seat-mode both
          --schedule MODE               cli|reversed|alternating-day (aliases: day-lead-reversed, alternating-start)

        Output:
          --output PATH                 JSONL path, or - for stdout (default: -)
          --event-probes PATH           Matched-Dawn branches for every remaining event (diagnostic only)
          --include-actions             Include full action traces in every JSONL match
          --trace PATH                  Pretty JSON trace for one representative match
          --save PATH                   Core Duck save for the same representative match
          --representative-seed N       Representative seed (default: seed-start)
          --representative-seat MODE    original|swapped (default: original)
          --progress-every N            Progress interval on stderr; 0 disables (default: 10)
          --help                        Show this help
        """;

    public static EvaluationOptions Parse(IReadOnlyList<string> args)
    {
        var seedStart = 0;
        var seedCount = 1;
        var policyA = "baseline";
        var policyB = "normal";
        var seatMode = EvaluationSeatMode.Original;
        var schedule = EvaluationSchedule.Cli;
        var output = "-";
        string? eventProbes = null;
        string? trace = null;
        string? save = null;
        int? representativeSeed = null;
        var representativeSeat = EvaluationSeatMode.Original;
        string? sourceLabel = null;
        var progressEvery = 10;
        var includeActions = false;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--seed-start": seedStart = Int(Value(args, ref index, arg), arg); break;
                case "--seed-count": seedCount = Int(Value(args, ref index, arg), arg); break;
                case "--policy-a": policyA = Value(args, ref index, arg); break;
                case "--policy-b": policyB = Value(args, ref index, arg); break;
                case "--seat-mode": seatMode = Seat(Value(args, ref index, arg), allowBoth: true); break;
                case "--swapped-seats": seatMode = EvaluationSeatMode.Both; break;
                case "--schedule": schedule = ParseSchedule(Value(args, ref index, arg)); break;
                case "--output": output = Value(args, ref index, arg); break;
                case "--event-probes": eventProbes = Value(args, ref index, arg); break;
                case "--trace": trace = Value(args, ref index, arg); break;
                case "--save": save = Value(args, ref index, arg); break;
                case "--representative-seed": representativeSeed = Int(Value(args, ref index, arg), arg); break;
                case "--representative-seat": representativeSeat = Seat(Value(args, ref index, arg), allowBoth: false); break;
                case "--source-label": sourceLabel = Value(args, ref index, arg); break;
                case "--progress-every": progressEvery = Int(Value(args, ref index, arg), arg); break;
                case "--include-actions": includeActions = true; break;
                case "--help": case "-h": showHelp = true; break;
                default: throw new ArgumentException("Unknown option: " + arg);
            }
        }

        if (showHelp)
            return new EvaluationOptions(seedStart, seedCount, policyA, policyB, seatMode, schedule, output,
                trace, save, representativeSeed, representativeSeat, sourceLabel ?? "help", progressEvery,
                includeActions, true);
        if (seedCount <= 0) throw new ArgumentOutOfRangeException(nameof(seedCount), "Seed count must be positive.");
        if (progressEvery < 0) throw new ArgumentOutOfRangeException(nameof(progressEvery));
        if (string.IsNullOrWhiteSpace(sourceLabel))
            throw new ArgumentException("--source-label is required for reproducible evidence.");
        _ = EvaluationPolicies.Create(policyA);
        _ = EvaluationPolicies.Create(policyB);
        if (representativeSeat == EvaluationSeatMode.Swapped && seatMode == EvaluationSeatMode.Original)
            throw new ArgumentException("A swapped representative requires --seat-mode swapped or both.");
        if (representativeSeat == EvaluationSeatMode.Original && seatMode == EvaluationSeatMode.Swapped)
            representativeSeat = EvaluationSeatMode.Swapped;
        return new EvaluationOptions(seedStart, seedCount, policyA, policyB, seatMode, schedule, output,
            trace, save, representativeSeed, representativeSeat, sourceLabel, progressEvery, includeActions, false) { EventProbesPath = eventProbes };
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count) throw new ArgumentException(option + " requires a value.");
        return args[index];
    }

    private static int Int(string value, string option) => int.TryParse(value, out var parsed)
        ? parsed : throw new ArgumentException(option + " requires an integer.");

    private static EvaluationSeatMode Seat(string value, bool allowBoth) => value switch
    {
        "original" => EvaluationSeatMode.Original,
        "swapped" => EvaluationSeatMode.Swapped,
        "both" when allowBoth => EvaluationSeatMode.Both,
        _ => throw new ArgumentException("Seat mode must be original" + (allowBoth ? ", swapped, or both." : " or swapped."))
    };

    private static EvaluationSchedule ParseSchedule(string value) => value switch
    {
        "cli" => EvaluationSchedule.Cli,
        "reversed" or "day-lead-reversed" => EvaluationSchedule.Reversed,
        "alternating-day" or "alternating-start" => EvaluationSchedule.AlternatingDay,
        _ => throw new ArgumentException("Schedule must be cli, reversed, or alternating-day.")
    };
}
