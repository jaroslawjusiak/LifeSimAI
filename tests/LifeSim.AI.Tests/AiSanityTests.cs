using FluentAssertions;
using LifeSim.AI;
using Xunit;

namespace LifeSim.AI.Tests;

public class AiSanityTests
{
    [Fact]
    public void AiAssembly_ShouldLoadSuccessfully()
    {
        typeof(AiMarker).Assembly.Should().NotBeNull();
    }
}
