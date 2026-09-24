using GatheringSeason.Core;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class CoreAssemblyTests
{
    [Fact]
    public void CoreAssemblyCanBeLoaded()
    {
        Assert.Equal("GatheringSeason.Core", typeof(CoreAssemblyMarker).Assembly.GetName().Name);
    }
}
