namespace GatheringSeason.Cli;

internal sealed class DuckCliOptions
{
    internal const string Usage = "Usage: GatheringSeason.Cli [--profile ducks] [--seed integer] [--players 2|3|4] [--wish-set set-1] [--inspect | --demo-day | --demo-game] [--save path | --no-save] [--continue | --new-game] [--two-player]";
    internal int? FixedSeed { get; private set; }
    internal bool InspectOnly { get; private set; }
    internal bool DemoDay { get; private set; }
    internal bool DemoGame { get; private set; }
    internal bool ContinueGame { get; private set; }
    internal bool NewGame { get; private set; }
    internal bool TwoPlayer { get; private set; }
    internal bool NoSave { get; private set; }
    internal int PlayerCount { get; private set; } = 2;
    internal string WishSetId { get; private set; } = "set-1";
    internal string? SavePath { get; private set; }

    internal bool ShouldSave => !NoSave && !InspectOnly
        && (ContinueGame || SavePath != null || (!DemoDay && !DemoGame));

    internal string EffectiveSavePath => SavePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GatheringSeason", "ducks-save.json");

    internal int CreateSeed(int? differentFrom = null)
    {
        if (FixedSeed.HasValue) return FixedSeed.Value;
        int seed;
        do { seed = Random.Shared.Next(); } while (seed == differentFrom);
        return seed;
    }

    internal static DuckCliOptions Parse(string[] arguments)
    {
        var options = new DuckCliOptions();
        for (var index = 0; index < arguments.Length; index++)
        {
            var parts = arguments[index].Split('=', 2);
            var name = parts[0];
            string Value()
            {
                if (parts.Length == 2 && parts[1].Length > 0) return parts[1];
                if (parts.Length == 2 || index + 1 >= arguments.Length)
                    throw new ArgumentException($"{name} requires a value.");
                return arguments[++index];
            }

            switch (name)
            {
                case "--seed":
                    if (!int.TryParse(Value(), out var seed)) throw new ArgumentException("--seed requires an integer.");
                    options.FixedSeed = seed;
                    break;
                case "--players":
                    if (!int.TryParse(Value(), out var playerCount) || playerCount is < 2 or > 4)
                        throw new ArgumentException("--players requires 2, 3 or 4.");
                    options.PlayerCount = playerCount;
                    break;
                case "--wish-set":
                    options.WishSetId = Value();
                    if (options.WishSetId != "set-1")
                        throw new ArgumentException("--wish-set currently supports set-1 only.");
                    break;
                case "--inspect" when parts.Length == 1: options.InspectOnly = true; break;
                case "--demo-day" when parts.Length == 1: options.DemoDay = true; break;
                case "--demo-game" when parts.Length == 1: options.DemoGame = true; break;
                case "--continue" when parts.Length == 1: options.ContinueGame = true; break;
                case "--new-game" when parts.Length == 1: options.NewGame = true; break;
                case "--two-player" when parts.Length == 1: options.TwoPlayer = true; break;
                case "--no-save" when parts.Length == 1: options.NoSave = true; break;
                case "--save":
                    options.SavePath = Value();
                    if (string.IsNullOrWhiteSpace(options.SavePath)) throw new ArgumentException("--save requires a file path.");
                    break;
                default: throw new ArgumentException($"Unknown duck option: {arguments[index]}.");
            }
        }
        if (new[] { options.InspectOnly, options.DemoDay, options.DemoGame }.Count(value => value) > 1)
            throw new ArgumentException("Choose one of --inspect, --demo-day or --demo-game.");
        if (options.ContinueGame && options.NewGame) throw new ArgumentException("Choose either --continue or --new-game.");
        if (options.NoSave && (options.SavePath != null || options.ContinueGame))
            throw new ArgumentException("--no-save cannot be combined with --save or --continue.");
        return options;
    }
}
