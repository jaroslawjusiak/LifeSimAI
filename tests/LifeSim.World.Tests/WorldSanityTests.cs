using FluentAssertions;
using LifeSim.World;
using Xunit;

namespace LifeSim.World.Tests;

public class WorldSanityTests
{
    [Fact]
    public void WorldAssembly_ShouldLoadSuccessfully()
    {
        typeof(WorldMarker).Assembly.Should().NotBeNull();
    }
}
