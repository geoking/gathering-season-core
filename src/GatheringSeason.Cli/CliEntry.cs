namespace GatheringSeason.Cli;

internal static class CliEntry
{
    private const string Usage = "Usage: GatheringSeason.Cli [--profile ducks] [options]";

    internal static int Run(string[] arguments)
    {
        if (arguments.Any(argument => argument is "--help" or "-h"))
        {
            ShowHelp();
            return 0;
        }

        try
        {
            var (profile, remaining) = SelectProfile(arguments);
            if (profile == "classic")
                throw new ArgumentException("The classic profile is no longer supported; Gathering Season is the only game.");
            if (profile != "ducks")
                throw new ArgumentException("--profile must be ducks.");
            return DuckCli.Run(remaining);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    private static (string Profile, string[] Remaining) SelectProfile(string[] arguments)
    {
        var profile = "ducks";
        var found = false;
        var remaining = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith("--profile=", StringComparison.Ordinal))
            {
                if (found) throw new ArgumentException("Specify --profile only once.");
                profile = argument.Substring("--profile=".Length);
                if (profile.Length == 0) throw new ArgumentException("--profile requires ducks.");
                found = true;
                continue;
            }
            if (argument == "--profile")
            {
                if (found) throw new ArgumentException("Specify --profile only once.");
                if (++index >= arguments.Length) throw new ArgumentException("--profile requires ducks.");
                profile = arguments[index];
                found = true;
                continue;
            }
            remaining.Add(argument);
        }
        return (profile, remaining.ToArray());
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Gathering Season command-line game");
        Console.WriteLine(Usage);
        Console.WriteLine();
        Console.WriteLine("Gathering Season's ten-Day duck game is the default. --profile ducks remains a compatibility alias.");
        Console.WriteLine("Options: --seed N, --inspect, --demo-day, --demo-game, --save PATH, --no-save,");
        Console.WriteLine("              --continue, --new-game, --two-player");
        Console.WriteLine("Use help during a duck game for its interactive commands.");
    }
}
