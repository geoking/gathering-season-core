using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed partial class DuckCliTests
{
    [Fact]
    public void Empty_resources_use_absence_copy_while_locations_and_free_prices_keep_their_meaning()
    {
        var result = Run("status\nshop\nq\n", "--seed", "42", "--no-save");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No Nest Twigs · space 0 · No Exhaustion", result.Output);
        Assert.Contains("No Trail Feathers", result.Output);
        Assert.Contains("No Companions", result.Output);
        Assert.Contains("seeds: Free", result.Output);
        Assert.DoesNotContain("0 Nest Twigs", result.Output);
        Assert.DoesNotContain("0 Trail Feathers", result.Output);
        Assert.DoesNotContain("0 Companions", result.Output);
    }

    [Fact]
    public void Night_ledger_distinguishes_absent_components_from_nonzero_rewards()
    {
        var result = Run("", "--seed", "42", "--demo-day");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Wildflowers: No Stars", result.Output);
        Assert.Contains("Reeds: No Twigs", result.Output);
        Assert.DoesNotContain("Wildflowers: +0", result.Output);
        Assert.DoesNotContain("Reeds: +0", result.Output);
        Assert.Contains("Star frozen", result.Output);
    }
}
