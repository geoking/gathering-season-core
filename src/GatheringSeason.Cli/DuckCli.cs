using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Cli;

internal static class DuckCli
{
    internal static int Run(string[] arguments)
    {
        try
        {
            var options = DuckCliOptions.Parse(arguments);
            var store = options.ShouldSave || options.ContinueGame
                ? new DuckSaveStore(options.EffectiveSavePath)
                : null;
            var resume = options.ContinueGame;
            if (!resume && !options.NewGame && !options.DemoDay && !options.DemoGame && store?.HasSavedGame == true)
            {
                Console.WriteLine("Saved game found. Enter c to Continue, n for a new game, or q to quit.");
                var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (choice != "c" && choice != "n") return 0;
                resume = choice == "c";
            }

            var recoveredBackup = false;
            int? seed = null;
            MatchSession<DuckMatchView> match;
            if (resume)
            {
                match = store!.Load(out recoveredBackup);
            }
            else
            {
                seed = options.CreateSeed();
                match = MatchSession.CreateDuck(seed.Value, new DuckMatchSettings(options.PlayerCount, options.WishSetId));
            }
            if (recoveredBackup)
                Console.Error.WriteLine("Recovered the previous saved action from backup; the latest primary save could not be read.");
            var initial = match.GetSnapshot("human");
            Console.WriteLine(resume
                ? $"Gathering Season · 10 Days · continuing saved game · {initial.Settings.PlayerCount} players · Wish Set {initial.Settings.WishSetId}"
                : $"Gathering Season · 10 Days · seed {seed} · {initial.Settings.PlayerCount} players · Wish Set {initial.Settings.WishSetId} · all ducks start at the nest");
            Console.WriteLine($"Rules revision {initial.RulesRevision} · {initial.Economy.CurrencyName}" +
                (initial.Economy.UsesStars ? " buy Wishes; Obstacles remain in your pouch." : " · legacy economy preserved for this saved game."));
            DuckCliRenderer.ShowStatus(initial);
            if (options.InspectOnly)
            {
                DuckCliRenderer.ShowCatalogue(match.GetSnapshot("human"));
                return 0;
            }
            if (options.DemoDay && match.GetSnapshot("human").Day != 1)
                throw new ArgumentException("--demo-day starts or continues Day 1 only; use --demo-game for the full match.");

            if (options.ShouldSave && !resume) store!.Save(match);
            if (options.ShouldSave) Console.WriteLine("Autosave: " + store!.SavePath);
            void SaveAfterAction() { if (options.ShouldSave) store!.Save(match); }
            if (options.DemoDay) return DuckCliDemos.RunDay(match, SaveAfterAction);
            if (options.DemoGame) return DuckCliDemos.RunGame(match, SaveAfterAction);
            return new DuckInteractiveCli(options, match, store, seed).Run();
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(DuckCliOptions.Usage);
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("Save/Continue failed: " + exception.Message);
            return 3;
        }
    }
}
