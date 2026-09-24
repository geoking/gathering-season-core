using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Cli;

internal static class DuckCliRenderer
{
    internal static void ShowStatus(DuckMatchView view, int recentHistory = 3)
    {
        Console.WriteLine($"Day {view.Day}/10 · {view.Phase} · Nest level {view.NestLevel} · World Event: {view.CurrentEvent.Name}");
        Console.WriteLine("  " + DuckReferenceText.Event(view.CurrentEvent.EventType, view.Rules));
        foreach (var player in view.Players)
        {
            var state = player.IsWornOut ? " · WORN OUT" : player.HasFinishedDay ? " · resting" : string.Empty;
            Console.WriteLine($"{player.Name}: {Quantity(player.TotalTwigs, "Nest Twig")} · space {player.Position} · Exhaustion {player.Exhaustion}/{player.SafeExhaustionMaximum} · pouch {Quantity(player.BagCount, "chip")}{state}");
            Console.WriteLine($"  Feather trail {Quantity(player.PermanentFeatherTrail, "Feather")} · today's start space {player.EffectiveStart} · active flock {Quantity(player.ActiveFlock, "Companion")}" +
                (player.ActiveMostRestedStep ? " · Most Rested head start +1" : string.Empty) +
                (player.PendingMostRestedStep ? " · Most Rested head start tomorrow" : string.Empty));
            if (player.HasGloriousSunshineChoice)
                Console.WriteLine("  Glorious Sunshine: " + (player.GloriousSunshineBenefit == DuckGloriousSunshineBenefit.FreshAir
                    ? $"Fresh Air (+{view.Rules.FreshAirExhaustionBonus} safe Exhaustion today)"
                    : $"Warm Dreams (+{CurrencyAmount(view.Economy.WarmDreamsReward, view.Economy)} tonight)"));
            var activeEffects = new List<string>();
            if (player.SplashProtectionArmed) activeEffects.Add("Splash protects the next placed chip");
            if (player.LogSlowdownPending) activeEffects.Add("Log will slow the next Wish");
            if (player.GuideProtectionAvailable) activeEffects.Add("Guide protects the first obstacle nuisance");
            if (activeEffects.Count > 0) Console.WriteLine("  Active: " + string.Join(" · ", activeEffects));
            if (view.Day > 1)
                Console.WriteLine($"  Dawn deficit {Quantity(player.DawnTwigDeficit, "Twig")} · Dawn delivery {Quantity(player.DawnFeathersAwarded, "Feather")}");
            ShowPlaced(player);
        }

        var own = view.Players.Single(player => player.Id == view.ViewerId);
        if (own.Position > 0) ShowPrintedReward(view.Rules.BoardSpaceAt(own.Position), "Your current printed reward", view.Economy);
        var nextShelter = view.Rules.BoardSpaces.FirstOrDefault(space => space.IsShelter && space.Space > own.Position);
        Console.WriteLine(nextShelter == null
            ? "Nearest forthcoming shelter: none; the route ends at space 43."
            : $"Nearest forthcoming shelter: {nextShelter.Space} {nextShelter.ShelterName} · printed {Reward(nextShelter, view.Economy)}");
        ShowPreview(view);
        foreach (var entry in view.History.TakeLast(recentHistory))
            Console.WriteLine($"  D{entry.Day} {(string.IsNullOrEmpty(entry.ActorId) ? "match" : entry.ActorId)}: {entry.Message}");
        ShowFinal(view);
        Console.WriteLine();
    }

    internal static void ShowBoard(DuckMatchView view, int? selectedSpace)
    {
        Console.WriteLine("Board rewards are printed values only; Core applies events, encounter bonuses, penalties and wear-out at Night.");
        var spaces = selectedSpace.HasValue
            ? view.Rules.BoardSpaces.Where(space => space.Space == selectedSpace.Value)
            : view.Rules.BoardSpaces;
        foreach (var space in spaces)
        {
            var markers = view.Players.Where(player => player.Position == space.Space).Select(player => player.Id == "human" ? "H" : "AI").ToArray();
            var marker = markers.Length == 0 ? "" : " [" + string.Join(",", markers) + "]";
            var shelter = space.IsShelter ? " · SHELTER: " + space.ShelterName : string.Empty;
            Console.WriteLine($"{space.Space,2}. {space.Biome,-9} · {Reward(space, view.Economy)}{shelter}{marker}");
        }
        if (selectedSpace.HasValue && !spaces.Any())
            Console.WriteLine("Choose a board space from 1 to 43.");
        Console.WriteLine("Markers: H = human, AI = Normal opponent. Rewards pay only where the duck finally rests.");
        Console.WriteLine("Twigs are the cumulative board total printed at that final space; do not add every space passed.");
        Console.WriteLine();
    }

    internal static void ShowBag(DuckMatchView view)
    {
        Console.WriteLine($"{Player(view).Name}'s private pouch information");
        ShowComposition("Remaining pouch", view.OwnBag);
        ShowComposition("Owned inventory", view.OwnInventory);
        Console.WriteLine("Remaining entries are counts sorted by name, never shuffled future order.");
        ShowPreview(view);
        Console.WriteLine();
    }

    internal static void ShowTokens(DuckMatchView view)
    {
        Console.WriteLine("All 16 encounter variants · Wishes and Obstacles");
        foreach (var encounter in view.Rules.EncounterDefinitions)
            Console.WriteLine($"{encounter.DefinitionId}: {encounter.Name} · {DuckReferenceText.Encounter(encounter, view.Rules)}");
        Console.WriteLine();
    }

    internal static void ShowShop(DuckMatchView view, IReadOnlyList<GameAction> legalActions)
    {
        var player = Player(view);
        var available = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter)
            .Select(action => action.DefinitionId).ToHashSet(StringComparer.Ordinal);
        var slots = Math.Max(0, player.PurchaseLimit - player.PurchasedEncounterDefinitionIds.Count);
        Console.WriteLine($"Dream shop · {CurrencyAmount(player.RemainingReward, view.Economy)} remaining · {slots}/{player.PurchaseLimit} {Plural(slots, "purchase slot")} remaining");
        foreach (var offer in view.ShopOffers)
        {
            var shopClosed = view.Phase != DuckPhase.Night || player.HasFinishedDream;
            var status = available.Contains(offer.DefinitionId) ? "AVAILABLE NOW"
                : shopClosed ? "unavailable in the current phase"
                : player.PurchasedShopTypes.Contains(offer.ShopType) ? "already bought this type tonight"
                : slots == 0 ? "no purchase slots remaining"
                : offer.Price > player.RemainingReward ? $"not enough {view.Economy.CurrencyName}"
                : "not currently offered as a legal action";
            Console.WriteLine($"{offer.DefinitionId}: {CurrencyAmount(offer.Price, view.Economy)} · {offer.Encounter.Name} · {status}");
            Console.WriteLine("  " + DuckReferenceText.Encounter(offer.Encounter, view.Rules));
        }
        if (view.Economy.UsesStars) Console.WriteLine("Choose Wishes with Stars. One free Seed still uses one purchase slot and your Seed choice for tonight.");
        Console.WriteLine("Prices above come from this running match; variants of Tailwind or Reeds share a one-per-type limit.");
        Console.WriteLine();
    }

    internal static void ShowEvent(DuckMatchView view)
    {
        Console.WriteLine($"World Event · Day {view.Day}: {view.CurrentEvent.Name}");
        Console.WriteLine(DuckReferenceText.Event(view.CurrentEvent.EventType, view.Rules));
        Console.WriteLine(view.CurrentEvent.EventType == DuckWorldEventType.GloriousSunshine
            ? "Both ducks publicly choose one benefit before either Adventure begins. The choice lasts only today."
            : "It applies to both ducks for this Day; collective conditions resolve after both finish.");
        Console.WriteLine();
    }

    internal static void ShowNight(DuckMatchView view)
    {
        var nights = view.Players.Where(player => player.LastNightOutcome != null).ToArray();
        if (nights.Length == 0)
        {
            Console.WriteLine("No Night has resolved yet.\n");
            return;
        }
        Console.WriteLine($"Night {nights[0].LastNightOutcome!.Day}:");
        foreach (var player in nights)
        {
            var night = player.LastNightOutcome!;
            Console.WriteLine($"{player.Name}: {CurrencyAmount(night.FrozenReward, view.Economy)} frozen; {CurrencyAmount(player.RemainingReward, view.Economy)} available now. Added to nest {Quantity(night.TotalTwigsEarned, "Twig")}; {Quantity(night.FeathersAwarded, "Feather")} awarded" +
                (night.IsMostRested ? " · Most Rested" : string.Empty));
            Console.WriteLine($"  {view.Economy.CurrencyName}: printed {night.PrintedReward}, Flowers +{night.FlowerReward}, final shelter +{night.FinalShelterReward}, collective event +{night.CollectiveEventReward}, Glorious Sunshine +{night.GloriousSunshineReward}, flock +{night.FlockReward}, Restless Night -{night.RestlessNightPenalty}, Pebbles -{night.PebblesPenalty}; before wear {night.RewardBeforeWear}.");
            Console.WriteLine($"  Twigs: printed {night.PrintedTwigs}, Reeds +{night.ReedsTwigs}, event +{night.EventTwigs}, Brambles -{night.BramblesPenalty}.");
            if (night.DreamTwigs > 0) Console.WriteLine($"  Dream Twigs: {Quantity(night.DreamTwigs, "Twig")}.");
        }
        Console.WriteLine();
    }

    internal static void ShowNewNight(DuckMatchView before, DuckMatchView after)
    {
        var previousDay = before.Players.Single(player => player.Id == "human").LastNightOutcome?.Day;
        var currentDay = after.Players.Single(player => player.Id == "human").LastNightOutcome?.Day;
        if (currentDay.HasValue && currentDay != previousDay) ShowNight(after);
    }

    internal static void ShowHistory(DuckMatchView view)
    {
        Console.WriteLine("Public match history");
        if (view.History.Count == 0) Console.WriteLine("  No completed actions yet.");
        foreach (var entry in view.History)
            Console.WriteLine($"  D{entry.Day} {(string.IsNullOrEmpty(entry.ActorId) ? "match" : entry.ActorId)}: {entry.Message}");
        Console.WriteLine();
    }

    internal static void ShowHelp(bool twoPlayer)
    {
        Console.WriteLine("Commands: action number, help, status, board [1-43], pouch, wishes/tokens, shop, event, night, history, r/restart, q/quit");
        Console.WriteLine(twoPlayer
            ? "Developer controls: human:N or ai:N executes that duck's issued action; view:human and view:ai select its private observation."
            : "view:human reviews your observation. The AI's private view is unavailable.");
        Console.WriteLine("Most Twigs wins. Five Exhaustion is normally safe; use tokens, event and board before deciding to draw again.");
        Console.WriteLine("Informational commands and invalid input do not advance either duck or write a save.\n");
    }

    internal static void ShowCatalogue(DuckMatchView view)
    {
        Console.WriteLine($"Catalogue: {Quantity(view.Rules.BoardSpaces.Count, "reward")} · {Quantity(view.Rules.BoardSpaces.Count(space => space.IsShelter), "shelter")} · {Quantity(view.Rules.EncounterDefinitions.Count, "encounter variant")} · {Quantity(view.ShopOffers.Count, "shop offer")} · {Quantity(view.Rules.WorldEvents.Count, "World Event")}");
        Console.WriteLine("Own inventory: " + string.Join(", ", view.OwnInventory.GroupBy(chip => chip.DefinitionId).Select(group => $"{group.Key} ×{group.Count()}")));
        foreach (var offer in view.ShopOffers)
            Console.WriteLine($"  {offer.DefinitionId}: {CurrencyAmount(offer.Price, view.Economy)} · movement {(offer.Encounter.BaseMovement?.ToString() ?? "flock-dependent")} · Twig yield {Quantity(offer.Encounter.TwigYield, "Twig")}");
    }

    private static DuckPlayerView Player(DuckMatchView view) => view.Players.Single(player => player.Id == view.ViewerId);

    private static void ShowPlaced(DuckPlayerView player)
    {
        if (player.PlacedChips.Count == 0) return;
        var placed = player.PlacedChips.Select(chip =>
        {
            var name = DuckRules.V1.Encounter(chip.DefinitionId).Name;
            return $"{name}@{chip.Position}" + (chip.NuisanceSuppressed ? "(protected)" : string.Empty);
        });
        Console.WriteLine("  Placed route: " + string.Join(" → ", placed));
    }

    private static void ShowComposition(string title, IReadOnlyList<DuckPhysicalChipView> chips)
    {
        var groups = chips.GroupBy(chip => chip.DefinitionId)
            .Select(group => (Name: DuckRules.V1.Encounter(group.Key).Name, Count: group.Count()))
            .OrderBy(group => group.Name, StringComparer.Ordinal);
        Console.WriteLine($"{title} ({Quantity(chips.Count, "chip")}): " + string.Join(", ", groups.Select(group => $"{group.Name} ×{group.Count}")));
    }

    private static void ShowPreview(DuckMatchView view)
    {
        if (view.KnownNextChips.Count == 0) return;
        Console.WriteLine($"Private Signpost preview for {view.ViewerId}, in order: " + string.Join(", ",
            view.KnownNextChips.Select(chip => DuckRules.V1.Encounter(chip.DefinitionId).Name)));
    }

    private static string Reward(DuckBoardSpace space, DuckEconomyDefinition economy) =>
        $"{CurrencyAmount(space.Reward, economy)} · {Quantity(space.Twigs, "Twig")}" +
        (space.Feathers > 0 ? $" · {Quantity(space.Feathers, "Feather")}" : string.Empty);

    private static void ShowPrintedReward(DuckBoardSpace space, string title, DuckEconomyDefinition economy) =>
        Console.WriteLine($"{title}: space {space.Space} {space.ShelterName ?? space.Biome.ToString()} · {Reward(space, economy)} (bonuses and wear-out excluded)");

    private static void ShowFinal(DuckMatchView view)
    {
        if (view.FinalResult is not { } result) return;
        Console.WriteLine($"Final standings · total Twigs, then Night 10 retained {view.Economy.CurrencyName}:");
        foreach (var standing in result.Standings)
            Console.WriteLine($"  {standing.Rank}. {standing.PlayerName}: {Quantity(standing.TotalTwigs, "Nest Twig")} (including {Quantity(standing.DreamTwigs, "Dream Twig")}), {CurrencyAmount(standing.FrozenNightTenReward, view.Economy)}");
        Console.WriteLine(result.WinnerIds.Count == 1
            ? "Winner: " + result.Standings.Single(standing => standing.IsWinner).PlayerName
            : "Draw: " + string.Join(", ", result.Standings.Where(standing => standing.IsWinner).Select(standing => standing.PlayerName)));
    }

    private static string CurrencyAmount(int amount, DuckEconomyDefinition economy) =>
        amount == 0 && economy.UsesStars
            ? "no Stars"
            : $"{amount} {(amount == 1 && economy.UsesStars ? "Star" : economy.CurrencyName)}";

    private static string Quantity(int amount, string singular) => $"{amount} {Plural(amount, singular)}";

    private static string Plural(int amount, string singular) => amount == 1 ? singular : singular + "s";
}
