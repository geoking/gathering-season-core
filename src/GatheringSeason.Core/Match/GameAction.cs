namespace GatheringSeason.Core.Match
{
    public enum GameActionKind
    {
        // Values 11-16 are part of the format-1 Duck save contract.
        Explore = 11,
        Settle = 12,
        BuyEncounter = 13,
        FinishDream = 14,
        NextDay = 15,
        ChooseEventBenefit = 16
    }

    /// <summary>A command offered by the current state. Execute validates its ID again; stale actions are rejected.</summary>
    public sealed class GameAction
    {
        private object? _scope;
        private string? _playerId;
        private long _revision;
        private string? _window;

        internal GameAction Issue(object scope, string playerId, long revision, string window)
        {
            var issued = new GameAction(Id, Kind, Label, Cost, DefinitionId);
            issued._scope = scope;
            issued._playerId = playerId;
            issued._revision = revision;
            issued._window = window;
            return issued;
        }

        internal bool WasIssued(object scope, string playerId, long revision, string window) =>
            ReferenceEquals(_scope, scope) && _playerId == playerId && _revision == revision && _window == window;

        internal GameAction(string id, GameActionKind kind, string label, int cost = 0, string definitionId = "")
        { Id = id; Kind = kind; Label = label; Cost = cost; DefinitionId = definitionId; }
        public string Id { get; }
        public GameActionKind Kind { get; }
        public string Label { get; }
        public int Cost { get; }
        public string DefinitionId { get; }
    }
}
