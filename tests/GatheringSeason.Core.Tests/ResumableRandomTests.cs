using GatheringSeason.Core.Randomness;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class ResumableRandomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void RestoringPortableStateReproducesFutureMixedBoundDraws(int seed)
    {
        var uninterrupted = new ResumableRandomSource(seed);
        for (var i = 0; i < 127; i++) uninterrupted.NextInt(13);
        var captured = uninterrupted.CaptureState();
        var restored = ResumableRandomSource.Restore(new RandomState(captured.Algorithm, captured.State, captured.Increment));
        foreach (var bound in Enumerable.Range(1, 50).Concat(new[] { int.MaxValue, 1073741825, 1 }))
        {
            for (var i = 0; i < 40; i++)
            {
                var expected = uninterrupted.NextInt(bound);
                Assert.InRange(expected, 0, bound - 1);
                Assert.Equal(expected, restored.NextInt(bound));
            }
        }
        Assert.Equal(uninterrupted.CaptureState().State, restored.CaptureState().State);
    }

    [Fact]
    public void CapturesAreDetachedAndInvalidBoundsDoNotAdvanceState()
    {
        var random = new ResumableRandomSource(42);
        var capture = random.CaptureState();
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(-1));
        Assert.Equal(capture.State, random.CaptureState().State);
        random.NextInt(10);
        Assert.NotEqual(capture.State, random.CaptureState().State);
        Assert.Throws<ArgumentException>(() => new RandomState("unknown", 0, 1));
        Assert.Throws<ArgumentException>(() => new RandomState(RandomState.CurrentAlgorithm, 0, 2));
    }

    [Fact]
    public void SameSeedReplaysAndDifferentSeedChangesTheSequence()
    {
        int[] Sequence(int seed)
        {
            var random = new ResumableRandomSource(seed);
            return Enumerable.Range(0, 100).Select(_ => random.NextInt(10000)).ToArray();
        }
        Assert.Equal(Sequence(12), Sequence(12));
        Assert.False(Sequence(12).SequenceEqual(Sequence(13)));
    }
}
