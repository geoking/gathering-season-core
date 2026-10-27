namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Immutable Duck match setup. Every player receives the same starting trail.</summary>
    public sealed class DuckMatchSettings
    {
        public const int StandardDays = 10;
        public const string StandardWishSetId = "set-1";
        public const string SecondWishSetId = "set-2";
        public const int CurrentWishSetVersion = 1;
        public static DuckMatchSettings Standard { get; } = new DuckMatchSettings();

        public DuckMatchSettings() : this(2, StandardWishSetId) { }

        public DuckMatchSettings(int playerCount = 2, string wishSetId = StandardWishSetId)
            : this(playerCount, wishSetId, CurrentWishSetVersion) { }

        public DuckMatchSettings(int playerCount, string wishSetId, int wishSetVersion)
        {
            if (playerCount < 2 || playerCount > 4)
                throw new System.ArgumentOutOfRangeException(nameof(playerCount), "A match needs two, three or four players.");
            if (wishSetId != StandardWishSetId && wishSetId != SecondWishSetId)
                throw new System.ArgumentException("Unsupported Wish set ID.", nameof(wishSetId));
            if (wishSetVersion != CurrentWishSetVersion)
                throw new System.ArgumentException("Unsupported Wish set version.", nameof(wishSetVersion));
            WishSetVersion = wishSetVersion;
            PlayerCount = playerCount;
            WishSetId = wishSetId;
        }

        public int PlayerCount { get; }
        public string WishSetId { get; }
        public int WishSetVersion { get; }
        public int Days => StandardDays;
        public int StartingFeathers => 0;
    }
}
