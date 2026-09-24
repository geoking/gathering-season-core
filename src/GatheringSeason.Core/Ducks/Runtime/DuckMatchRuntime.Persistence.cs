using System;
using GatheringSeason.Core.Randomness;

namespace GatheringSeason.Core.Ducks.Runtime
{
    internal sealed partial class DuckMatchRuntime
    {
        private DuckMatchRuntime(DuckMatchState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Rules = Definitions.DuckRules.ForRulesRevision(state.RulesRevision);
            _random = ResumableRandomSource.Restore(state.RandomState);
        }

        internal static DuckMatchRuntime Restore(DuckMatchState state)
        {
            return new DuckMatchRuntime(state);
        }
    }
}
