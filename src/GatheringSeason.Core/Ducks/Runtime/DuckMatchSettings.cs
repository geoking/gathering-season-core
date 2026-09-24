namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Immutable v1 Duck match setup. Both players receive the same starting trail.</summary>
    public sealed class DuckMatchSettings
    {
        public const int StandardDays = 10;
        public static DuckMatchSettings Standard { get; } = new DuckMatchSettings();

        public DuckMatchSettings() { }

        public int Days => StandardDays;
        public int StartingFeathers => 0;
    }
}
