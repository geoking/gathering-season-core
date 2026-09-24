using GatheringSeason.Core.Ducks.Runtime;

namespace GatheringSeason.Core.Match
{
    public static class MatchSession
    {
        public static MatchSession<DuckMatchView> CreateDuck(int seed, DuckMatchSettings? settings = null)
        {
            return new MatchSession<DuckMatchView>(DuckMatchRuntime.Create(seed, settings));
        }
    }
}
