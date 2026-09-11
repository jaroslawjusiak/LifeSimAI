using FluentAssertions;
using LifeSim.Core.Diagnostics;
using Xunit;

namespace LifeSim.Core.Tests;

public class TurnCorrelationTests
{
    [Fact]
    public void Mint_ReturnsDistinctIds()
    {
        TurnCorrelation.Mint().Should().NotBe(TurnCorrelation.Mint());
    }

    [Fact]
    public void Begin_WithExplicitId_MakesItCurrent()
    {
        using (TurnCorrelation.Begin("turn-1"))
        {
            TurnCorrelation.Current.Should().Be("turn-1");
        }

        TurnCorrelation.Current.Should().BeNull();
    }

    [Fact]
    public void Begin_WithoutId_MintsId()
    {
        using (TurnCorrelation.Begin())
        {
            TurnCorrelation.Current.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Begin_FlowsAcrossAwait()
    {
        using (TurnCorrelation.Begin("turn-async"))
        {
            await Task.Yield();
            TurnCorrelation.Current.Should().Be("turn-async");
        }
    }

    [Fact]
    public void Dispose_RestoresPreviousId()
    {
        using (TurnCorrelation.Begin("outer"))
        {
            using (TurnCorrelation.Begin("inner"))
            {
                TurnCorrelation.Current.Should().Be("inner");
            }

            TurnCorrelation.Current.Should().Be("outer");
        }
    }
}
