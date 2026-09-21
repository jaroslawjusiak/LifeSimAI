using FluentAssertions;
using LifeSim.Core.Entities;
using Xunit;

namespace LifeSim.Core.Tests;

public class LocationTests
{
    [Fact]
    public void IsOpen_AlwaysTrue_WhenNoOpenHours()
    {
        var location = new Location("a", "A", []);

        location.IsOpen(3).Should().BeTrue();
        location.IsOpen(23).Should().BeTrue();
    }

    [Fact]
    public void IsOpen_RespectsOpenHours()
    {
        var location = new Location("a", "A", [], openHours: new TimeWindow(9, 17));

        location.IsOpen(9).Should().BeTrue();
        location.IsOpen(16).Should().BeTrue();
        location.IsOpen(17).Should().BeFalse();
        location.IsOpen(8).Should().BeFalse();
    }

    [Fact]
    public void OneWayConnection_IsNotAutomaticallyReciprocated()
    {
        var a = new Location("a", "A", [new LocationConnection("b")]);
        var b = new Location("b", "B", []);

        a.Connections.Should().ContainSingle(c => c.TargetId == "b");
        b.Connections.Should().BeEmpty();
    }

    [Fact]
    public void Connections_SupportGatedEdges()
    {
        var location = new Location("a", "A", [new LocationConnection("b", "apartment-key")]);

        location.Connections.Single().RequiresFlag.Should().Be("apartment-key");
    }

    [Fact]
    public void AllowedActionIds_AreExposed()
    {
        var location = new Location("a", "A", [], ["talk", "work"]);

        location.AllowedActionIds.Should().Equal("talk", "work");
    }
}
