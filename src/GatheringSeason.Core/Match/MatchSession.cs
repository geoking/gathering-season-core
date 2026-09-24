using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GatheringSeason.Core.Match
{
    /// <summary>
    /// Shared authoritative command boundary. Profiles own their state and phase
    /// rules; clients receive only detached observations and issued commands.
    /// </summary>
    public sealed partial class MatchSession<TView>
    {
        private readonly IMatchRuntime<TView> _runtime;
        private readonly object _commandScope = new object();
        private readonly Dictionary<string, long> _revisions = new Dictionary<string, long>(StringComparer.Ordinal);

        internal MatchSession(IMatchRuntime<TView> runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public TView GetSnapshot(string playerId) => _runtime.GetSnapshot(playerId);

        public IReadOnlyList<GameAction> GetLegalActions(string playerId)
        {
            var actions = _runtime.GetLegalActions(playerId);
            var revision = Revision(playerId);
            return new ReadOnlyCollection<GameAction>(actions.Select(action =>
                action.Issue(_commandScope, playerId, revision, _runtime.ActionWindow)).ToList());
        }

        public TView Execute(string playerId, GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!action.WasIssued(_commandScope, playerId, Revision(playerId), _runtime.ActionWindow))
                throw new InvalidOperationException("This action is stale or belongs to another player or match. Request current legal actions.");
            var legal = _runtime.GetLegalActions(playerId).SingleOrDefault(candidate => candidate.Id == action.Id);
            if (legal == null) throw new InvalidOperationException($"Action '{action.Id}' is no longer legal for {playerId}.");
            var view = _runtime.Execute(playerId, legal);
            _revisions[playerId] = Revision(playerId) + 1;
            return view;
        }

        private long Revision(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            return _revisions.TryGetValue(playerId, out var revision) ? revision : 0;
        }
    }

}
