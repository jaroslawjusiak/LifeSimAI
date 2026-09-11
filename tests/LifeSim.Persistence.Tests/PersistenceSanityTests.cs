using FluentAssertions;
using LifeSim.Persistence;
using Xunit;

namespace LifeSim.Persistence.Tests;

public class PersistenceSanityTests
{
    [Fact]
    public void PersistenceAssembly_ShouldLoadSuccessfully()
    {
        typeof(PersistenceMarker).Assembly.Should().NotBeNull();
    }
}
