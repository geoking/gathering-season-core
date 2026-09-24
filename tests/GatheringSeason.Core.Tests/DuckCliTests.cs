using System.Diagnostics;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed partial class DuckCliTests
{
    [Fact]
    public void Default_route_and_ducks_alias_open_Gathering_Season_while_classic_is_unsupported()
    {
        var ducks = Run("", "--seed", "42", "--inspect");
        Assert.Equal(0, ducks.ExitCode);
        Assert.Contains("Gathering Season · 10 Days · seed 42", ducks.Output);
        Assert.Contains("Rules revision 7 · Stars", ducks.Output);

        var alias = Run("", "--profile", "ducks", "--seed", "42", "--inspect");
        Assert.Equal(ducks.Output, alias.Output);

        var classic = Run("", "--profile", "classic", "--seed", "42");
        Assert.Equal(2, classic.ExitCode);
        Assert.Contains("classic profile is no longer supported", classic.Error);
        Assert.DoesNotContain("nine-round", classic.Output);
    }

    [Fact]
    public void Launch_help_exits_without_creating_or_loading_a_save()
    {
        using var files = new SaveFiles();
        var result = Run("", "--save", files.Path, "--continue", "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ten-Day duck game is the default", result.Output);
        Assert.Contains("--profile ducks remains a compatibility alias", result.Output);
        Assert.DoesNotContain("Classic options", result.Output);
        Assert.False(File.Exists(files.Path));
        Assert.False(File.Exists(files.Path + ".bak"));
    }

    [Fact]
    public void Invalid_profile_fails_before_starting_a_game()
    {
        var result = Run("", "--profile", "unknown");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--profile must be ducks", result.Error);
        Assert.DoesNotContain("Gathering Season", result.Output);
        Assert.DoesNotContain("classic reference", result.Output);
    }

    [Theory]
    [InlineData("pouch")]
    [InlineData("bag")]
    public void Interactive_references_are_observation_driven_and_keep_the_AI_view_private(string pouchCommand)
    {
        var input = $"status\nboard 4\nboard 16\nboard 17\nboard 21\n{pouchCommand}\nwishes\nshop\nevent\nnight\nhistory\nview:ai\nq\n";
        var result = Run(input, "--seed", "42", "--no-save");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("4. Wetlands", result.Output);
        Assert.Contains("2 Stars · 1 Twig · 1 Feather · SHELTER: Reed hammock", result.Output);
        Assert.Contains("16. Meadow", result.Output);
        Assert.Contains("2 Stars · 4 Twigs", result.Output);
        Assert.Contains("17. Meadow", result.Output);
        Assert.Contains("3 Stars · 4 Twigs · 1 Feather · SHELTER: Clover hollow", result.Output);
        Assert.Contains("21. Meadow", result.Output);
        Assert.Contains("3 Stars · 5 Twigs · 1 Feather · SHELTER: Orchard shelter", result.Output);
        Assert.Contains("One free Seed still uses one purchase slot", result.Output);
        Assert.Contains("extra Flowers do not stack", result.Output);
        Assert.Contains("Remaining pouch", result.Output);
        Assert.Contains("All 16 encounter variants", result.Output);
        Assert.Contains("reeds_3: Nesting reeds 3", result.Output);
        Assert.Contains("Dream shop", result.Output);
        Assert.Contains("World Event · Day 1: Thick Morning Mist", result.Output);
        Assert.Contains("No Night has resolved yet", result.Output);
        Assert.Contains("Public match history", result.Output);
        Assert.Contains("AI's private view is unavailable", result.Output);
        Assert.DoesNotContain("Private Signpost preview for ai", result.Output);
    }

    [Fact]
    public void Human_and_Normal_make_visible_Glorious_Sunshine_choices_before_Adventure()
    {
        var result = Run("2\nq\n", "--seed", "10", "--no-save");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("World Event: Glorious Sunshine", result.Output);
        Assert.Contains("AI: Fresh Air (+2 safe Exhaustion today)", result.Output);
        Assert.Contains("1. Fresh Air (+2 safe Exhaustion today)", result.Output);
        Assert.Contains("2. Warm Dreams (+2 Stars tonight)", result.Output);
        Assert.Contains("Glorious Sunshine: Warm Dreams (+2 Stars tonight)", result.Output);
        Assert.Contains("Glorious Sunshine: Fresh Air (+2 safe Exhaustion today)", result.Output);
    }

    [Fact]
    public void Interactive_shop_for_a_revision_one_save_uses_its_live_prices()
    {
        using var files = new SaveFiles();
        var fixture = Path.Combine(RepositoryRoot(), "tests", "GatheringSeason.Core.Tests", "Fixtures", "Duck", "seed20-day10-revision1.json");
        File.Copy(fixture, files.Path);
        var result = Run("shop\nboard 43\ntokens\nq\n", "--continue", "--two-player", "--save", files.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Dream shop", result.Output);
        Assert.Contains("Rules revision 1 · Sleep", result.Output);
        Assert.DoesNotContain("Stars", result.Output);
        Assert.Contains("tailwind_2: 5 Sleep", result.Output);
        Assert.Contains("reeds_3: 16 Sleep", result.Output);
        Assert.DoesNotContain("reeds_3: 20 Sleep", result.Output);
        Assert.Contains("21 Sleep · 9 Twigs", result.Output);
        Assert.Contains("+2 Sleep at Night for each placed Wildflowers", result.Output);
    }

    [Fact]
    public void Explicit_seed_replays_the_same_catalogue_output()
    {
        var first = Run("", "--seed", "42", "--inspect");
        var second = Run("", "--profile=ducks", "--seed=42", "--inspect");
        Assert.Equal(first.Output, second.Output);
    }

    [Fact]
    public void Daily_demo_runs_real_issued_actions_through_Night_purchases_and_next_Dawn()
    {
        var result = Run("", "--profile", "ducks", "--seed", "42", "--demo-day");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Day 1/10 · Adventure", result.Output);
        Assert.Contains("Day 1/10 · Night", result.Output);
        Assert.Contains("Night 1:", result.Output);
        Assert.Contains("Star frozen", result.Output);
        Assert.Contains("Twigs: printed", result.Output);
        Assert.Contains("human: Buy Wish Seeds for no Stars", result.Output);
        Assert.Contains("ai: Buy Wish Seeds for no Stars", result.Output);
        Assert.Contains("Day 2/10 · Adventure", result.Output);
        Assert.Contains("pouch 14", result.Output);
        Assert.Contains("Dawn deficit", result.Output);
        Assert.Contains("Day 1 → Night 1 → Day 2", result.Output);
        Assert.DoesNotContain("Day 3/10", result.Output);
        Assert.DoesNotContain("Private preview for ai", result.Output);
    }

    [Fact]
    public void Inspect_exposes_the_duck_profile_without_classic_currencies()
    {
        var result = Run("", "--profile", "ducks", "--seed", "42", "--inspect");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("43 rewards · 8 shelters · 16 encounter variants · 11 shop offers · 10 World Events", result.Output);
        Assert.Contains("pouch 13", result.Output);
        Assert.Contains("reeds_3: 4 Stars · movement 1 · Twig yield 3 Twigs", result.Output);
        Assert.DoesNotContain("rubies", result.Output);
        Assert.DoesNotContain("coins", result.Output);
        Assert.DoesNotContain("Private preview", result.Output);
    }

    [Theory]
    [InlineData("--starting-feathers", "4")]
    [InlineData("--starting-feathers", "3")]
    [InlineData("--seed", "not-an-integer")]
    [InlineData("--unexpected", "option")]
    public void Invalid_duck_options_fail_clearly(string option, string value)
    {
        var result = Run("", "--profile", "ducks", option, value);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage: GatheringSeason.Cli --profile ducks", result.Error);
        Assert.DoesNotContain("Gathering Season ·", result.Output);
    }

    private static (int ExitCode, string Output, string Error) Run(string input, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "run", "--project", "src/GatheringSeason.Cli", "--configuration", "Release", "--verbosity", "quiet", "--" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Duck CLI did not exit.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GatheringSeason.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }
}
