using FluentAssertions;
using LifeSim.Core.Entities;
using LifeSim.Core.Stats;
using Xunit;

namespace LifeSim.Core.Tests;

public class WorldStateTests
{
    private static StatSet Stats() => new([new StatDef("energy", 0m, 100m)]);

    private static WorldState Build() => new(
        new Player("Alex", Stats(), new SkillSet([]), 100m, "apartment"),
        [new Npc("mia", "Mia", Stats(), new WeeklySchedule("cafe", []))],
        [new Location("apartment", "Apartment", [])],
        [new Item("coffee", "Coffee")],
        [new SkillDef("coding", "Coding", [100], [])],
        []);

    [Fact]
    public void LookupById_ReturnsEntity()
    {
        var world = Build();

        world.GetNpc("mia").Should().NotBeNull();
        world.GetLocation("apartment").Should().NotBeNull();
        world.GetItem("coffee").Should().NotBeNull();
        world.GetSkill("coding").Should().NotBeNull();
    }

    [Fact]
    public void Lookup_UnknownId_ReturnsNull()
    {
        var world = Build();

        world.GetNpc("nope").Should().BeNull();
        world.GetLocation("nope").Should().BeNull();
        world.GetItem("nope").Should().BeNull();
        world.GetSkill("nope").Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsDuplicateNpcIds()
    {
        Npc npc() => new("mia", "Mia", Stats(), new WeeklySchedule("cafe", []));

        var act = () => new WorldState(
            new Player("Alex", Stats(), new SkillSet([]), 100m, "apartment"),
            [npc(), npc()],
            [],
            [],
            [],
            []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsDuplicateLocationIds()
    {
        var act = () => new WorldState(
            new Player("Alex", Stats(), new SkillSet([]), 100m, "apartment"),
            [],
            [new Location("a", "A", []), new Location("a", "A2", [])],
            [],
            [],
            []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Npc_LocationAt_DelegatesToSchedule()
    {
        var npc = new Npc(
            "mia", "Mia", Stats(),
            new WeeklySchedule("cafe", [new ScheduleEntry(new HashSet<DayOfWeek> { DayOfWeek.Monday }, 9, 17, "office")]));

        npc.LocationAt(DayOfWeek.Monday, 12).Should().Be("office");
        npc.LocationAt(DayOfWeek.Monday, 20).Should().Be("cafe");
    }
}
