using GatheringSeason.Core.Match;

namespace GatheringSeason.Evaluation.Policies;

public sealed record EvaluationPolicyDecision(GameAction Action, string Reason);
