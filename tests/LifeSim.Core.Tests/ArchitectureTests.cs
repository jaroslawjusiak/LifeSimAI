using FluentAssertions;
using LifeSim.Core;
using Xunit;

namespace LifeSim.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void LifeSimCore_ShouldNotReference_HigherLayerAssemblies()
    {
        // Core must have zero outward dependencies towards UI, AI, World, Persistence, or Spectre
        var coreAssembly = typeof(CoreMarker).Assembly;
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        var forbiddenNames = new[]
        {
            "LifeSim.Console",
            "LifeSim.AI",
            "LifeSim.World",
            "LifeSim.Persistence",
            "Spectre.Console",
            "Microsoft.Extensions.Options"
        };

        foreach (var referenced in referencedAssemblies)
        {
            foreach (var forbidden in forbiddenNames)
            {
                referenced.Name.Should().NotContain(
                    forbidden,
                    because: "LifeSim.Core must remain completely isolated from UI, AI, World, and presentation frameworks"
                );
            }
        }
    }

    [Fact]
    public void LifeSimCore_ShouldOnlyReference_BclAndItself()
    {
        // The domain project may depend on the BCL and itself — nothing else.
        var coreAssembly = typeof(CoreMarker).Assembly;

        coreAssembly.GetReferencedAssemblies().Should().OnlyContain(referenced =>
            referenced.Name != null &&
            (referenced.Name.StartsWith("System", StringComparison.Ordinal) ||
             referenced.Name.StartsWith("netstandard", StringComparison.Ordinal) ||
             referenced.Name.StartsWith("mscorlib", StringComparison.Ordinal) ||
             referenced.Name.StartsWith("LifeSim.Core", StringComparison.Ordinal)),
            because: "LifeSim.Core is the innermost layer and must not pull in third-party or higher-layer packages");
    }
}
