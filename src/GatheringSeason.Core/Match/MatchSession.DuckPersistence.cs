using System;
using System.Collections.Generic;
using GatheringSeason.Core.Ducks.Runtime;

namespace GatheringSeason.Core.Match
{
    public sealed partial class MatchSession<TView>
    {
        internal DuckMatchRuntime DuckRuntime()
        {
            return _runtime as DuckMatchRuntime
                ?? throw new ArgumentException("The session does not use the Duck rules profile.");
        }

        internal IReadOnlyDictionary<string, long> CaptureCommandRevisions(IEnumerable<string> playerIds)
        {
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var playerId in playerIds)
                result.Add(playerId, Revision(playerId));
            return result;
        }

        internal static MatchSession<DuckMatchView> RestoreDuck(
            DuckMatchRuntime runtime,
            IReadOnlyDictionary<string, long> revisions)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (revisions == null) throw new ArgumentNullException(nameof(revisions));
            var session = new MatchSession<DuckMatchView>(runtime);
            foreach (var revision in revisions)
                session._revisions.Add(revision.Key, revision.Value);
            return session;
        }
    }
}
