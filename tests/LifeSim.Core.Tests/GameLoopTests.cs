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
        [new ActionDefinition("idle", ActionVerb.Custom, "Test", 0, 0m, 0m, [], [new ActionEffect.StatDelta("energy", -10m)])],
        new GameClock(0, 9, 0));

    // The world catalog knows 'cooking' but the player's SkillSet does not, so applying the
    // action throws inside ActionResolver.Apply (not inside Validate) — a deterministic way to
    // force a failure while the loop is in the Applying state.
    private static WorldState ApplyingThrowWorld() => new(
        new Player("Alex", new StatSet([new StatDef("energy", 0m, 100m)]), new SkillSet([]), 100m, "flat"),
        [],
        [new Location("flat", "Flat", [])],
        [],
        [new SkillDef("cooking", "Cooking", [100], [])],
        [new ActionDefinition("cook", ActionVerb.Custom, "Test", 0, 0m, 0m, [], [new ActionEffect.SkillXp("cooking", 5)])],
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

    // Updated for D4 (AGENTS rule 9 — the old assertion enshrined the defect). A translator that
    // throws must NOT fall back to resolving the raw input as an action id; the turn fails with a
    // typed Translating error and leaves the world untouched.
    [Fact]
    public void RunTurn_RecoversTranslatorFailure_UsesFallback()
    {
        var loop = new GameLoop(World());
        loop.Translator = (_, _) => throw new InvalidOperationException("boom");

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeFalse();
        result.ActionId.Should().BeNull();
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
        var world = World();
        var loop = new GameLoop(world);

        var result = loop.RunTurn("nope");

        result.IsSuccess.Should().BeFalse();
        result.ActionResult!.Reasons.Should().ContainSingle(r => r.Contains("nope"));
        world.Player.Stats.ValueOf("energy").Should().Be(100m); // validation failed read-only
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

    // ── D1: each stage is entered before its work runs ────────────────────────

    [Fact]
    public void RunTurn_EntersEachDelegateStage_WithStateSetToThatStage()
    {
        var loop = new GameLoop(World());
        var observed = new Dictionary<TurnState, TurnState>();

        loop.Translator = (_, _) =>
        {
            observed[TurnState.Translating] = loop.State;
            return new TranslatedCommand("idle");
        };
        loop.WorldTick = _ => observed[TurnState.WorldTick] = loop.State;
        loop.Narrator = _ =>
        {
            observed[TurnState.Narrating] = loop.State;
            return "ok";
        };
        loop.OptionsGenerator = _ =>
        {
            observed[TurnState.Options] = loop.State;
            return ["next"];
        };

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeTrue();
        observed[TurnState.Translating].Should().Be(TurnState.Translating);
        observed[TurnState.WorldTick].Should().Be(TurnState.WorldTick);
        observed[TurnState.Narrating].Should().Be(TurnState.Narrating);
        observed[TurnState.Options].Should().Be(TurnState.Options);
    }

    [Fact]
    public void RunTurn_JournalsStageTransitionsInExecutionOrder()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.Translator = (_, _) => new TranslatedCommand("idle");

        loop.RunTurn("idle");

        world.Journal.ByType(JournalEntryTypes.StageTransition)
            .Select(e => e.Payload)
            .Should().Equal(
                "InputReceived",
                "Translating",
                "Validating",
                "Applying",
                "WorldTick",
                "Narrating",
                "Options",
                "Idle");
    }

    [Fact]
    public void RunTurn_ValidationWork_RunsWhileStateIsValidating()
    {
        var world = World();
        var loop = new GameLoop(world);
        // An empty action id makes ActionResolver.Validate throw, so the failure is raised
        // while the loop is inside the Validating stage.
        loop.Translator = (_, _) => new TranslatedCommand(string.Empty);

        var result = loop.RunTurn("anything");

        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.Validating);
        var entry = world.Journal.Entries
            .First(e => e.Type == JournalEntryTypes.StageTransition && e.Payload == "Validating");
        var failure = world.Journal.Entries
            .First(e => e.Type == JournalEntryTypes.StageFailed);
        failure.Payload.Should().StartWith("Validating");
        entry.Seq.Should().BeLessThan(failure.Seq);
    }

    [Fact]
    public void RunTurn_ApplicationWork_RunsWhileStateIsApplying()
    {
        var world = ApplyingThrowWorld();
        var loop = new GameLoop(world);

        var result = loop.RunTurn("cook");

        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.Applying);
        var entry = world.Journal.Entries
            .First(e => e.Type == JournalEntryTypes.StageTransition && e.Payload == "Applying");
        var failure = world.Journal.Entries
            .First(e => e.Type == JournalEntryTypes.StageFailed);
        failure.Payload.Should().StartWith("Applying");
        entry.Seq.Should().BeLessThan(failure.Seq);
    }

    // ── D2: a refused transition is a typed failure that stops the turn ───────

    [Fact]
    public void RunTurn_RefusedTransition_ReturnsTypedFailureAndStopsPipeline()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.TryTransition(TurnState.InputReceived).Should().BeTrue();

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeFalse();
        result.FinalState.Should().Be(TurnState.Idle);
        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.InputReceived);
        loop.State.Should().Be(TurnState.Idle);
        world.Journal.ByType(JournalEntryTypes.ActionResolved).Should().BeEmpty();
        world.Player.Stats.ValueOf("energy").Should().Be(100m);
    }

    // ── D3: the journal matches what actually executed ────────────────────────

    [Fact]
    public void RunTurn_EscapingException_RecordsRealStageNotApplying()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.Translator = (_, _) => throw new InvalidOperationException("boom");

        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.Translating);
        result.Errors.Should().NotContain(e => e.Stage == TurnState.Applying);
        world.Journal.ByType(JournalEntryTypes.StageFailed)
            .Should().ContainSingle()
            .Which.Payload.Should().Be("Translating: boom");
    }

    [Fact]
    public void RunTurn_JournalsRawInputAndTranslatedCommand()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.Translator = (_, _) => new TranslatedCommand("idle");

        loop.RunTurn("do the thing");

        world.Journal.ByType(JournalEntryTypes.RawInput)
            .Should().ContainSingle()
            .Which.Payload.Should().Be("do the thing");
        world.Journal.ByType(JournalEntryTypes.TranslatedCommand)
            .Should().ContainSingle()
            .Which.Payload.Should().Be("idle");
    }

    [Fact]
    public void RunTurn_StageFailure_IsJournaledWithStageAndError()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.Narrator = _ => throw new InvalidOperationException("narr");

        loop.RunTurn("idle");

        world.Journal.ByType(JournalEntryTypes.StageFailed)
            .Should().ContainSingle()
            .Which.Payload.Should().Be("Narrating: narr");
    }

    // ── D4: a failed translator never executes raw text as an action id ───────

    [Fact]
    public void RunTurn_TranslatorFailure_DoesNotExecuteRawInputAsAction()
    {
        var world = World();
        var loop = new GameLoop(world);
        loop.Translator = (_, _) => throw new InvalidOperationException("boom");

        // "idle" is a valid action id in this world; with the old fallback it would have run.
        var result = loop.RunTurn("idle");

        result.IsSuccess.Should().BeFalse();
        result.ActionResult.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Stage == TurnState.Translating);
        world.Journal.ByType(JournalEntryTypes.ActionResolved).Should().BeEmpty();
        world.Player.Stats.ValueOf("energy").Should().Be(100m);
    }
}
