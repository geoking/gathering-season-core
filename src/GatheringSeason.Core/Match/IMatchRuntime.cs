using System.Collections.Generic;

namespace GatheringSeason.Core.Match
{
    /// <summary>Profile-owned state and phase rules behind the shared command boundary.</summary>
    internal interface IMatchRuntime<TView>
    {
        // Changes when a new phase, Day, or simultaneous-decision beat begins.
        string ActionWindow { get; }
        TView GetSnapshot(string playerId);
        IReadOnlyList<GameAction> GetLegalActions(string playerId);
        TView Execute(string playerId, GameAction action);
    }
}
