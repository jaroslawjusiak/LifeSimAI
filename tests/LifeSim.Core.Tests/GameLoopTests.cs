using FluentAssertions;
using LifeSim.Core.Actions;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;
using LifeSim.Core.Stats;
using LifeSim.Core.Time;
using LifeSim.Core.Turns;
using Xunit;

namespace LifeSim.Core.Tests;

public class GameLoopTests
{
    private static WorldState World() => new(
        new Player("Alex", new StatSet([new StatDef("energy", 0m, 100m)]), new SkillSet([]), 100m, "flat"),
        [],
        [new Location("flat", "Flat", [])],
        [],
        [],
        [new ActionDefinition("idle", ActionVerb.Custom, "Test", 0, 0m, 0m, [], [])],
        new GameClock(0, 9, 0));

    [Fact]
    public void TryTransition_AllowsLegalNextState()
    {
        var loop = new GameLoop(World());

        loop.TryTransition(TurnState.InputReceived).Should().BeTrue();
        loop.State.Should().Be(TurnState.InputReceived);
    }

    [Fact]
    public void TryTransition_RejectsIllegalTransition()
    {
        var loop = new GameLoop(World());

        loop.TryTransition(TurnState.Applying).Should().BeFalse();
        loop.State.Should().Be(TurnState.Idle);
    }

    [Fact]
    public void RunTurn_DrivesPipelineHeadless_EndToEnd()
    {
        var loop = new GameLoop(World());

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeTrue();
        result.FinalState.Should().Be(TurnState.Idle);
        result.ActionId.Should().Be("idle");
    }

    [Fact]
    public void RunTurn_RecoversTranslatorFailure_UsesFallback()
    {
        var loop = new GameLoop(World());
        loop.Translator = (_, _) => throw new InvalidOperationException("boom");

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.Translating);
    }

    [Fact]
    public void RunTurn_RecoversNarratorAndOptionsFailures()
    {
        var loop = new GameLoop(World());
        loop.Narrator = _ => throw new InvalidOperationException("narr");
        loop.OptionsGenerator = _ => throw new InvalidOperationException("opt");

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeTrue();
        result.Narration.Should().BeEmpty();
        result.Options.Should().BeEmpty();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void RunTurn_UnknownAction_ReturnsFailureWithoutThrowing()
    {
        var loop = new GameLoop(World());

        var result = loop.RunTurn("nope");

        result.IsSuccess.Should().BeFalse();
        result.ActionResult!.Reasons.Should().ContainSingle(r => r.Contains("nope"));
    }

    [Fact]
    public void RunTurn_AllEntriesOfATurn_ShareOneCorrelationId()
    {
        var world = World();
        var loop = new GameLoop(world);

        loop.RunTurn("idle");
        loop.RunTurn("idle");

        var entries = world.Journal.Entries;
        entries.Should().NotBeEmpty();
        entries.Select(e => e.CorrelationId).Distinct().Should().HaveCount(2);
        entries.Should().OnlyContain(e => !string.IsNullOrEmpty(e.CorrelationId));
    }
}
