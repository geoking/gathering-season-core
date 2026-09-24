using System.Collections.Generic;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Core.Ducks.AI
{
    /// <summary>A Duck policy sees only one detached observation and its currently issued actions.</summary>
    public interface IDuckPlayerPolicy
    {
        GameAction Choose(DuckMatchView observation, IReadOnlyList<GameAction> legalActions);
    }
}
