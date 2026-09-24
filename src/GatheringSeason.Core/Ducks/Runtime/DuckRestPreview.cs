using System;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Whether a conditional Night bonus can still be earned by resting now.</summary>
    public enum DuckRestBonusStatus
    {
        Unavailable,
        Possible,
        Guaranteed
    }

    /// <summary>
    /// Read-only reward bounds for resting on the viewer's occupied space during Adventure.
    /// TwigsEarnedToday includes Twigs already banked from Reeds and events; the signed
    /// NestTwigChangeOnRest is the additional change when Night resolves. Stars fields
    /// use the saved catalogue's Night currency, called Sleep in early revisions.
    /// </summary>
    public sealed class DuckRestPreview
    {
        internal DuckRestPreview(
            int twigsEarnedToday,
            int nestTwigChangeOnRest,
            int feathersAwarded,
            int starsMinimum,
            int starsMaximum,
            int dreamTwigsMinimum,
            int dreamTwigsMaximum,
            int sharedEventStars,
            DuckRestBonusStatus sharedEventStatus,
            int flockStars,
            DuckRestBonusStatus flockStatus,
            DuckRestBonusStatus mostRestedStatus)
        {
            if (starsMinimum < 0 || starsMaximum < starsMinimum)
                throw new ArgumentOutOfRangeException(nameof(starsMinimum));
            if (dreamTwigsMinimum < 0 || dreamTwigsMaximum < dreamTwigsMinimum)
                throw new ArgumentOutOfRangeException(nameof(dreamTwigsMinimum));
            TwigsEarnedToday = twigsEarnedToday;
            NestTwigChangeOnRest = nestTwigChangeOnRest;
            FeathersAwarded = feathersAwarded;
            StarsMinimum = starsMinimum;
            StarsMaximum = starsMaximum;
            DreamTwigsMinimum = dreamTwigsMinimum;
            DreamTwigsMaximum = dreamTwigsMaximum;
            SharedEventStars = sharedEventStars;
            SharedEventStatus = sharedEventStatus;
            FlockStars = flockStars;
            FlockStatus = flockStatus;
            MostRestedStatus = mostRestedStatus;
        }

        public int TwigsEarnedToday { get; }
        public int NestTwigChangeOnRest { get; }
        public int FeathersAwarded { get; }
        public int StarsMinimum { get; }
        public int StarsMaximum { get; }
        public int DreamTwigsMinimum { get; }
        public int DreamTwigsMaximum { get; }
        public int SharedEventStars { get; }
        public DuckRestBonusStatus SharedEventStatus { get; }
        public int FlockStars { get; }
        public DuckRestBonusStatus FlockStatus { get; }
        public DuckRestBonusStatus MostRestedStatus { get; }
    }
}
