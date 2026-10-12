using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class ActionCompatibilityTests
{
    [Fact]
    public void Action_kinds_preserve_supported_save_numeric_values()
    {
        Assert.Equal(
            new[]
            {
                GameActionKind.Explore, GameActionKind.Settle, GameActionKind.BuyEncounter,
                GameActionKind.FinishDream, GameActionKind.NextDay, GameActionKind.ChooseEventBenefit
            },
            Enum.GetValues<GameActionKind>());
        Assert.Equal(new[] { 11, 12, 13, 14, 15, 16 }, Enum.GetValues<GameActionKind>().Select(value => (int)value));
    }
}
