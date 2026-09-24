using System;

namespace GatheringSeason.Core.Randomness
{
    /// <summary>Portable PCG32 state. Hosts may serialize this data without saving a runtime object.</summary>
    public sealed class RandomState
    {
        public const string CurrentAlgorithm = "pcg32-v1";
        public RandomState(string algorithm, ulong state, ulong increment)
        {
            if (algorithm != CurrentAlgorithm) throw new ArgumentException("Unsupported random algorithm.", nameof(algorithm));
            if ((increment & 1UL) == 0) throw new ArgumentException("PCG stream increment must be odd.", nameof(increment));
            Algorithm = algorithm; State = state; Increment = increment;
        }
        public string Algorithm { get; }
        public ulong State { get; }
        public ulong Increment { get; }
    }

    /// <summary>Deterministic bounded draws with explicit state for exact future continuation.</summary>
    public sealed class ResumableRandomSource : IRandomSource
    {
        private ulong _state;
        private readonly ulong _increment;

        public ResumableRandomSource(int seed)
        {
            _increment = 109;
            NextUInt();
            _state = unchecked(_state + (uint)seed);
            NextUInt();
        }

        private ResumableRandomSource(RandomState state)
        {
            _state = state.State;
            _increment = state.Increment;
        }

        public RandomState CaptureState() => new RandomState(RandomState.CurrentAlgorithm, _state, _increment);
        public static ResumableRandomSource Restore(RandomState state) =>
            new ResumableRandomSource(state ?? throw new ArgumentNullException(nameof(state)));

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            var bound = (uint)exclusiveMax;
            var threshold = unchecked(0u - bound) % bound;
            uint sample;
            do { sample = NextUInt(); } while (sample < threshold);
            return (int)(sample % bound);
        }

        private uint NextUInt()
        {
            var old = _state;
            _state = unchecked(old * 6364136223846793005UL + _increment);
            var shifted = (uint)(((old >> 18) ^ old) >> 27);
            var rotation = (int)(old >> 59);
            return (shifted >> rotation) | (shifted << ((-rotation) & 31));
        }
    }
}
