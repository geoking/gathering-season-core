using GatheringSeason.Core.Ducks.AI;
using GatheringSeason.Core.Ducks.Definitions;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Evaluation.Policies;

public interface IEvaluationPolicy
{
    string Id { get; }
    string Description { get; }
    EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions);
}

public static class EvaluationPolicies
{
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[]
    {
        "baseline", "normal", "cautious", "adventurous",
        "movement-heavy", "reeds-heavy", "flowers-first", "seeds-first", "sunshine-fresh", "sunshine-warm", "sandbag-day3"
    });

    public static IEvaluationPolicy Create(string id) => id switch
    {
        "baseline" => new BaselineAdapter(),
        "normal" => new NormalAdapter(),
        "cautious" => new CautiousPolicy(),
        "adventurous" => new AdventurousPolicy(),
        "movement-heavy" => new PurchasePreferencePolicy(PurchasePreference.Movement),
        "reeds-heavy" => new PurchasePreferencePolicy(PurchasePreference.Reeds),
        "sandbag-day3" => new SandbagPolicy(),
        "flowers-first" => new TypeFirstPolicy(DuckEncounterType.Wildflowers, id),
        "seeds-first" => new TypeFirstPolicy(DuckEncounterType.Seeds, id),
        "sunshine-fresh" => new SunshinePolicy(DuckGloriousSunshineBenefit.FreshAir, id),
        "sunshine-warm" => new SunshinePolicy(DuckGloriousSunshineBenefit.WarmDreams, id),
        _ => throw new ArgumentException(
            $"Unknown policy '{id}'. Expected one of: {string.Join(", ", Ids)}.", nameof(id))
    };

    private sealed class BaselineAdapter : IEvaluationPolicy
    {
        private readonly M4BaselinePolicy _policy = new();
        public string Id => "baseline";
        public string Description => $"Frozen M4 Normal baseline (source SHA-256 {M4BaselinePolicy.SourceSha256}).";
        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions) =>
            _policy.Evaluate(observation, legalActions);
    }

    private sealed class NormalAdapter : IEvaluationPolicy
    {
        private readonly DuckNormalPolicy _policy = new();
        public string Id => "normal";
        public string Description => "Current Core DuckNormalPolicy candidate.";
        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            var decision = _policy.Evaluate(observation, legalActions);
            return new EvaluationPolicyDecision(decision.Action, decision.Reason);
        }
    }

    private sealed class CautiousPolicy : IEvaluationPolicy
    {
        public string Id => "cautious";
        public string Description => "Settles at Exhaustion 3, or at a shelter from Exhaustion 2; shops cheaply with protection first.";

        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            Validate(observation, legalActions);
            var explore = Action(legalActions, GameActionKind.Explore);
            var settle = Action(legalActions, GameActionKind.Settle);
            if (explore != null && settle == null)
                return Decision(explore, "The first draw is required.");
            if (explore != null && settle != null)
            {
                var player = observation.Players.Single(candidate => candidate.Id == observation.ViewerId);
                var isShelter = player.Position > 0 && observation.Rules.BoardSpaceAt(player.Position).IsShelter;
                if (player.Exhaustion >= 3 || (isShelter && player.Exhaustion >= 2))
                    return Decision(settle, isShelter
                        ? "The cautious reference protects a shelter rest at Exhaustion 2 or more."
                        : "The cautious reference settles at Exhaustion 3 or more.");
                return Decision(explore, "The cautious reference continues below its Exhaustion threshold.");
            }
            if (settle != null)
                return Decision(settle, "No further exploration action is available.");

            var buy = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter)
                .OrderBy(action => SafetyOrder(observation.Rules.ShopOffer(action.DefinitionId).ShopType))
                .ThenBy(action => observation.Rules.ShopOffer(action.DefinitionId).Price)
                .ThenBy(action => action.DefinitionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (buy != null)
                return Decision(buy, "The cautious reference buys the cheapest available protection-oriented offer.");
            return Fallback(legalActions, "The cautious reference finishes the current phase.");
        }

        private static int SafetyOrder(DuckEncounterType type) => type switch
        {
            DuckEncounterType.Splash => 0,
            DuckEncounterType.Signpost => 1,
            DuckEncounterType.Seeds => 2,
            DuckEncounterType.Wildflowers => 3,
            DuckEncounterType.Companion => 4,
            DuckEncounterType.Tailwind => 5,
            _ => 6
        };
    }

    private sealed class AdventurousPolicy : IEvaluationPolicy
    {
        public string Id => "adventurous";
        public string Description => "Explores until Exhaustion reaches the current safe maximum; buys the highest-priced legal offer.";

        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            Validate(observation, legalActions);
            var explore = Action(legalActions, GameActionKind.Explore);
            var settle = Action(legalActions, GameActionKind.Settle);
            if (explore != null && settle != null)
            {
                var player = observation.Players.Single(candidate => candidate.Id == observation.ViewerId);
                if (player.Exhaustion >= player.SafeExhaustionMaximum)
                    return Decision(settle, "The adventurous reference settles at its current safe Exhaustion maximum.");
                return Decision(explore, "The adventurous reference continues below its current safe Exhaustion maximum.");
            }
            if (explore != null)
                return Decision(explore, "The first draw is required.");
            var buy = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter)
                .OrderByDescending(action => observation.Rules.ShopOffer(action.DefinitionId).Price)
                .ThenBy(action => action.DefinitionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (buy != null)
                return Decision(buy, "The adventurous reference buys the highest-priced legal offer.");
            return Fallback(legalActions, "The adventurous reference finishes the current phase.");
        }
    }

    private enum PurchasePreference { Movement, Reeds }

    private sealed class PurchasePreferencePolicy : IEvaluationPolicy
    {
        private readonly PurchasePreference _preference;
        private readonly NormalAdapter _normal = new();

        public PurchasePreferencePolicy(PurchasePreference preference) => _preference = preference;
        public string Id => _preference == PurchasePreference.Movement ? "movement-heavy" : "reeds-heavy";
        public string Description => _preference == PurchasePreference.Movement
            ? "Uses current Normal adventure choices and prioritises the largest legal movement purchase."
            : "Uses current Normal adventure choices and prioritises the largest legal Reeds purchase.";

        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            Validate(observation, legalActions);
            var buys = legalActions.Where(action => action.Kind == GameActionKind.BuyEncounter).ToArray();
            if (buys.Length == 0) return _normal.Decide(observation, legalActions);

            GameAction? preferred = _preference == PurchasePreference.Reeds
                ? buys.Where(action => observation.Rules.ShopOffer(action.DefinitionId).ShopType == DuckEncounterType.Reeds)
                    .OrderByDescending(action => observation.Rules.ShopOffer(action.DefinitionId).Encounter.TwigYield)
                    .ThenByDescending(action => observation.Rules.ShopOffer(action.DefinitionId).Price)
                    .FirstOrDefault()
                : buys.Where(action => observation.Rules.ShopOffer(action.DefinitionId).ShopType != DuckEncounterType.Reeds)
                    .OrderByDescending(action => Movement(observation.Rules.ShopOffer(action.DefinitionId).Encounter))
                    .ThenByDescending(action => observation.Rules.ShopOffer(action.DefinitionId).Price)
                    .ThenBy(action => action.DefinitionId, StringComparer.Ordinal)
                    .FirstOrDefault();
            if (preferred == null) return _normal.Decide(observation, legalActions);
            return Decision(preferred, _preference == PurchasePreference.Movement
                ? "The movement-heavy diagnostic selects the largest legal movement offer."
                : "The Reeds-heavy diagnostic selects the largest legal Reeds yield.");
        }

        private static int Movement(DuckEncounterDefinition encounter) =>
            encounter.EncounterType == DuckEncounterType.Companion ? 4 : encounter.BaseMovement ?? 0;
    }

    private sealed class TypeFirstPolicy(DuckEncounterType type, string id) : IEvaluationPolicy
    {
        private readonly NormalAdapter _normal = new();
        public string Id => id;
        public string Description => $"Always chooses a legal {type} purchase first, then uses Normal.";
        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            var preferred = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.BuyEncounter
                && observation.Rules.ShopOffer(action.DefinitionId).ShopType == type);
            return preferred == null ? _normal.Decide(observation, legalActions)
                : Decision(preferred, $"The {Id} diagnostic reserves its first available slot for {type}.");
        }
    }

    private sealed class SunshinePolicy(DuckGloriousSunshineBenefit benefit, string id) : IEvaluationPolicy
    {
        private readonly NormalAdapter _normal = new();
        public string Id => id;
        public string Description => $"Forces Sunshine {benefit}; every other decision uses Normal.";
        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            var choice = legalActions.FirstOrDefault(action => action.Kind == GameActionKind.ChooseEventBenefit
                && action.Id.EndsWith(benefit == DuckGloriousSunshineBenefit.FreshAir ? ".fresh-air" : ".warm-dreams", StringComparison.Ordinal));
            return choice == null ? _normal.Decide(observation, legalActions)
                : Decision(choice, $"Controlled Sunshine choice: {benefit}.");
        }
    }

    private sealed class SandbagPolicy : IEvaluationPolicy
    {
        private readonly NormalAdapter _normal = new();
        public string Id => "sandbag-day3";
        public string Description => "Takes only the required first draw through Day 3, then uses current Normal.";

        public EvaluationPolicyDecision Decide(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
        {
            Validate(observation, legalActions);
            if (observation.Day > 3 || observation.Phase != DuckPhase.Adventure)
                return _normal.Decide(observation, legalActions);
            var settle = Action(legalActions, GameActionKind.Settle);
            if (settle != null)
                return Decision(settle, "The sandbag control settles after one draw through Day 3.");
            var explore = Action(legalActions, GameActionKind.Explore);
            return explore != null
                ? Decision(explore, "The first draw is required before the sandbag control may settle.")
                : _normal.Decide(observation, legalActions);
        }
    }

    private static void Validate(DuckMatchView observation, IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(legalActions);
        if (legalActions.Count == 0) throw new ArgumentException("A policy needs an issued legal action.", nameof(legalActions));
    }

    private static GameAction? Action(IReadOnlyList<GameAction> actions, GameActionKind kind) =>
        actions.FirstOrDefault(action => action.Kind == kind);

    private static EvaluationPolicyDecision Fallback(IReadOnlyList<GameAction> actions, string reason) =>
        Decision(actions.First(), reason);

    private static EvaluationPolicyDecision Decision(GameAction action, string reason) => new(action, reason);
}
