using FluentAssertions;
using LifeSim.Core.Actions;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;
using LifeSim.Core.Turns;
using Xunit;

namespace LifeSim.Core.Tests;

public class DemoWorldPlaythroughTests
{
    private static void PlayUntilDay(WorldState world, GameLoop loop, int targetDay)
    {
        while (world.Clock.DayIndex < targetDay)
        {
            loop.RunTurn("sleep");
            loop.RunTurn("move_to_office");
            loop.RunTurn("work");
            loop.RunTurn("move_to_flat");
            loop.RunTurn("eat");
            loop.RunTurn("rest");
        }
    }

    [Fact]
    public void SevenDayScriptedPlaythrough_KeepsStatsAlive_AndEarnsMoney()
    {
        var world = DemoWorld.Create();
        var loop = new GameLoop(world);

        PlayUntilDay(world, loop, 7);

        world.Clock.DayIndex.Should().BeGreaterThanOrEqualTo(7);
        world.Player.Stats.ValueOf("energy").Should().BeGreaterThan(0);
        world.Player.Stats.ValueOf("hunger").Should().BeGreaterThan(0);
        // Changed 100 -> 85 (AGENTS rule 9): ADR-011 makes decay boundary-based, so this scripted
        // loop now crosses more hour boundaries than the old `minutes / 60` chunk count did. The
        // extra decay drives hunger to 0, and the pre-existing Starve consequence (untouched by
        // this story) drains health by 5/hour. 85 is the deterministic outcome under the ruled
        // semantics and remains > 0, so the "keeps stats alive" property this test guards holds.
        world.Player.Stats.ValueOf("health").Should().Be(85m);
        world.Player.Money.Should().BeGreaterThan(100m);
    }

    [Fact]
    public void Replay_ReapplyingJournal_ReproducesStateHash()
    {
        var original = DemoWorld.Create();
        var loop = new GameLoop(original);
        PlayUntilDay(original, loop, 3);

        var replay = DemoWorld.Create();
        foreach (var entry in original.Journal.ByType(JournalEntryTypes.ActionResolved))
        {
            var result = ActionResolver.Resolve(replay, entry.Payload!);
            result.IsSuccess.Should().BeTrue();
        }

        StateHasher.Compute(replay).Should().Be(StateHasher.Compute(original));
    }
}
