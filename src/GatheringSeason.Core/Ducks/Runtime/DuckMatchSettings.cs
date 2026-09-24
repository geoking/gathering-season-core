namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Immutable Duck match setup. Every player receives the same starting trail.</summary>
    public sealed class DuckMatchSettings
    {
        public const int StandardDays = 10;
        public const string StandardWishSetId = "set-1";
        public static DuckMatchSettings Standard { get; } = new DuckMatchSettings();

        public DuckMatchSettings() : this(2, StandardWishSetId) { }

        public DuckMatchSettings(int playerCount = 2, string wishSetId = StandardWishSetId)
        {
            if (playerCount < 2 || playerCount > 4)
                throw new System.ArgumentOutOfRangeException(nameof(playerCount), "A match needs two, three or four players.");
            if (wishSetId != StandardWishSetId)
                throw new System.ArgumentException("Unsupported Wish set ID.", nameof(wishSetId));
            PlayerCount = playerCount;
            WishSetId = wishSetId;
        }

        public int PlayerCount { get; }
        public string WishSetId { get; }
        public int Days => StandardDays;
        public int StartingFeathers => 0;
    }
}
