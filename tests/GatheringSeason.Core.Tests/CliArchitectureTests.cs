using System;
using System.IO;
using GatheringSeason.Core;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class CliArchitectureTests
{
    [Fact]
    public void Cli_uses_only_the_Gathering_Season_session_boundary()
    {
        var cliDirectory = Path.Combine(FindRepositoryRoot(), "src", "GatheringSeason.Cli");
        var sources = Directory.GetFiles(cliDirectory, "*.cs")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.Ordinal);
        var allText = string.Join("\n", sources.Values);

        Assert.Contains("CliEntry.Run(args)", sources["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("MatchSession.CreateDuck", sources["DuckCli.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("ClassicCli", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchSession.Create(", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("starting-rubies", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_exposes_only_duck_actions_and_no_classic_rules_namespaces()
    {
        var assembly = typeof(CoreAssemblyMarker).Assembly;
        var classicNamespaces = new[]
        {
            "GatheringSeason.Core.AI", "GatheringSeason.Core.Bags", "GatheringSeason.Core.Cauldrons",
            "GatheringSeason.Core.Game", "GatheringSeason.Core.Players", "GatheringSeason.Core.Rewards",
            "GatheringSeason.Core.Rounds", "GatheringSeason.Core.Rules", "GatheringSeason.Core.Tokens"
        };

        Assert.DoesNotContain(assembly.GetTypes(), type => classicNamespaces.Contains(type.Namespace));
        Assert.Equal(
            new[]
            {
                GameActionKind.Explore, GameActionKind.Settle, GameActionKind.BuyEncounter,
                GameActionKind.FinishDream, GameActionKind.NextDay, GameActionKind.ChooseEventBenefit
            },
            Enum.GetValues<GameActionKind>());
        Assert.Equal(new[] { 11, 12, 13, 14, 15, 16 }, Enum.GetValues<GameActionKind>().Select(value => (int)value));
        Assert.Null(typeof(GameAction).GetProperty("Color"));
        Assert.Null(typeof(GameAction).GetProperty("ChoiceTitle"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GatheringSeason.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GatheringSeason.sln.");
    }
}
