using System;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.AI
{
    /// <summary>An issued action together with a short explanation for tests and developer clients.</summary>
    public sealed class DuckPolicyDecision
    {
        internal DuckPolicyDecision(GameAction action, string reason)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Reason = string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("A policy decision needs a reason.", nameof(reason))
                : reason;
        }

        public GameAction Action { get; }
        public string Reason { get; }
    }
}
