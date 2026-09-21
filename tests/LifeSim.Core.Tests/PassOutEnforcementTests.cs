using FluentAssertions;
using LifeSim.Core.Actions;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;
using LifeSim.Core.Rules;
using LifeSim.Core.Stats;
using LifeSim.Core.Time;
using Xunit;

namespace LifeSim.Core.Tests;

/// <summary>
/// D6 / ADR-012 enforcement of the pass-out consequence. A <see cref="StatConsequence.PassOut"/>
/// crossing only latches pending (mutating nothing); the forced sleep and the once-only mood
/// penalty are drained by <see cref="ActionResolver"/> after the action's own advance, through
/// the same clock dispatcher, so decay ticks exactly once per boundary (no re-entrancy, no
/// double decay) and the outcome re-derives deterministically from the replayed actions.
/// </summary>
public class PassOutEnforcementTests
{
    // ── Fixture ──────────────────────────────────────────────────────────────

    private static StatDef Energy(decimal decayPerHour = 2m) =>
        new("energy", 0m, 100m, DecayPerHour: decayPerHour, CriticalAt: 0m, Consequence: StatConsequence.PassOut);

    private static StatDef Mood(decimal decayPerHour = 0m) =>
        new("mood", 0m, 100m, DecayPerHour: decayPerHour);

    private static StatDef Focus(decimal decayPerHour = 1m) =>
        new("focus", 0m, 100m, DecayPerHour: decayPerHour);

    private static StatDef Vigor() =>
        new("vigor", 0m, 100m, DecayPerHour: 1m, CriticalAt: 3m, Consequence: StatConsequence.PassOut);

    private static Dictionary<string, decimal> Initial(params (string Id, decimal Value)[] values) =>
        values.ToDictionary(v => v.Id, v => v.Value);

    private static WorldState Build(
        IReadOnlyList<StatDef> defs,
        IReadOnlyDictionary<string, decimal> initial,
        WorldRules? rules = null,
        GameClock? clock = null,
        params ActionDefinition[] actions) =>
        new(
            new Player("Alex", new StatSet(defs, initial), new SkillSet([]), 100m, "flat"),
            [],
            [new Location("flat", "Flat", [])],
            [],
            [],
            actions,
            clock ?? new GameClock(0, 8, 0),
            rules);

    private static ActionDefinition Act(
        string id,
        int timeCost = 0,
        decimal energyCost = 0m,
        ActionEffect? effect = null) =>
        new(id, ActionVerb.Custom, "Test", timeCost, energyCost, 0m, [], effect is null ? [] : [effect]);

    private static StatDef[] StandardDefs(decimal moodDecay = 0m) =>
        [Energy(), Mood(moodDecay), Focus()];

    private static int PassOutCount(WorldState world) =>
        world.Journal.ByType(JournalEntryTypes.PassOut).Count;

    private static IReadOnlyList<string> JournalTypes(WorldState world) =>
        world.Journal.Entries.Select(e => e.Type).ToList();

    // ── Representation (AC 1) ────────────────────────────────────────────────

    [Fact]
    public void WorldRules_Default_HasRuledValues()
    {
        var rules = WorldRules.Default;

        rules.ForcedSleepHours.Should().Be(6);
        rules.PassOutMoodPenalty.Should().Be(-10m);
        rules.PassOutMoodStatId.Should().Be("mood");
    }

    [Fact]
    public void WorldRules_NegativeForcedSleepHours_Throws()
    {
        var act = () => new WorldRules(forcedSleepHours: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WorldRules_PositiveMoodPenalty_Throws()
    {
        var act = () => new WorldRules(passOutMoodPenalty: 1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WorldRules_BlankMoodStatId_Throws()
    {
        var act = () => new WorldRules(passOutMoodStatId: " ");

        act.Should().Throw<ArgumentException>();
    }

    // ── Trigger: latch only, mutate nothing (AC 3) ───────────────────────────

    [Fact]
    public void StatCriticalHandler_PassOut_LatchesOnly_NoClockOrStatMutation()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 50m), ("focus", 100m)),
            actions: [Act("wait")]);
        var clockBefore = world.Clock;

        // A PassOut crossing outside an action must not sleep, decay or penalise anything.
        world.Player.Stats.ApplyDelta("energy", -100m);

        world.Clock.Should().Be(clockBefore);
        world.Player.Stats.ValueOf("mood").Should().Be(50m);
        world.Player.Stats.ValueOf("focus").Should().Be(100m);
        PassOutCount(world).Should().Be(0);

        // The latch persists: the next applied action drains it.
        ActionResolver.Resolve(world, "wait"); // timeCost 0

        world.Clock.Should().Be(new GameClock(0, 14, 0));
        world.Player.Stats.ValueOf("mood").Should().Be(40m);
        world.Player.Stats.ValueOf("focus").Should().Be(94m);
        PassOutCount(world).Should().Be(1);
    }

    // ── Trigger: hourly decay vs action cost (AC 4) ──────────────────────────

    [Fact]
    public void PassOut_TriggeredByHourlyDecay_DrainsAfterTheAdvance()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 1m), ("mood", 100m), ("focus", 100m)),
            actions: [Act("wait", timeCost: 60)]);

        ActionResolver.Resolve(world, "wait");

        // action 8:00 -> 9:00 (energy crosses on the 9:00 boundary), then 6h forced sleep.
        world.Clock.Should().Be(new GameClock(0, 15, 0));
        world.Player.Stats.ValueOf("energy").Should().Be(0m);
        world.Player.Stats.ValueOf("focus").Should().Be(93m); // 1 action tick + 6 sleep ticks
        PassOutCount(world).Should().Be(1);
        world.Journal.ByType(JournalEntryTypes.PassOut).Single().Payload.Should().Be("energy:6");
    }

    // ── Application: exactly N dispatcher ticks, no double decay (AC 5, AC 12) ──

    [Fact]
    public void PassOut_ForcedSleep_AdvancesExactlyRulesHours_AndDecaysOncePerBoundary()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(0, 14, 0));
        // Exactly six HourPassed ticks reach ApplyHourPassed; a hand-rolled loop would double this.
        world.Player.Stats.ValueOf("focus").Should().Be(94m);
    }

    [Fact]
    public void PassOut_TotalDecayEqualsActionPlusSleepBoundaries()
    {
        // 14:30 -> 16:00 (2 boundaries) then 6h sleep (6 boundaries) = 8 decay ticks total.
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            clock: new GameClock(0, 14, 30),
            actions: [Act("exhaust", timeCost: 90, energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(0, 22, 0));
        world.Player.Stats.ValueOf("focus").Should().Be(92m);
    }

    [Fact]
    public void LongerForcedSleep_CrossesSeveralBoundaries_AndDecaysOnceEach()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            rules: new WorldRules(forcedSleepHours: 10),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(0, 18, 0));
        world.Player.Stats.ValueOf("focus").Should().Be(90m);
    }

    [Fact]
    public void PassOut_HonoursConfiguredWorldRules()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            rules: new WorldRules(forcedSleepHours: 3, passOutMoodPenalty: -5m),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(0, 11, 0));
        world.Player.Stats.ValueOf("mood").Should().Be(95m);
        world.Journal.ByType(JournalEntryTypes.PassOut).Single().Payload.Should().Be("energy:3");
    }

    // ── Application: mood penalty exactly once, clamped, absent = no-op (AC 6) ──

    [Fact]
    public void PassOut_MoodPenalty_IsAppliedExactlyOnce()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Player.Stats.ValueOf("mood").Should().Be(90m);
    }

    [Fact]
    public void PassOut_MoodPenalty_ClampsToStatFloor()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 5m), ("focus", 100m)),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Player.Stats.ValueOf("mood").Should().Be(0m);
    }

    [Fact]
    public void PassOut_AbsentMoodStat_IsANoOp()
    {
        var world = Build(
            [Energy(), Focus()],
            Initial(("energy", 100m), ("focus", 100m)),
            actions: [Act("exhaust", energyCost: 100m)]);

        var result = ActionResolver.Resolve(world, "exhaust");

        result.IsSuccess.Should().BeTrue();
        PassOutCount(world).Should().Be(1);
        world.Player.Stats.ValueOf("focus").Should().Be(94m);
    }

    // ── Application: action not rolled back, no energy restored (AC 7) ───────

    [Fact]
    public void PassOut_DoesNotRollBackActionEffects_AndRestoresNoEnergy()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            actions: [Act("shift", energyCost: 100m, effect: new ActionEffect.Money(100m))]);

        var result = ActionResolver.Resolve(world, "shift");

        result.IsSuccess.Should().BeTrue();
        world.Player.Money.Should().Be(200m); // effect kept
        world.Player.Stats.ValueOf("energy").Should().Be(0m); // consequence does not restore
        world.Clock.Should().Be(new GameClock(0, 14, 0));
    }

    // ── Re-entrancy: no recursion, crossings during sleep are deferred (AC 8) ──

    [Fact]
    public void PassOut_DoesNotRecurse_AndDefersCrossingsRaisedDuringSleep()
    {
        // 'vigor' crosses its PassOut threshold on the 5th hour of the forced sleep. That crossing
        // must NOT trigger a second forced sleep inside the same drain; it is deferred to the next
        // applied action.
        var world = Build(
            [Energy(), Mood(), Focus(), Vigor()],
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m), ("vigor", 8m)),
            actions: [Act("exhaust", energyCost: 100m), Act("wait")]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(0, 14, 0), because: "only one forced sleep may run per drain");
        PassOutCount(world).Should().Be(1);
        world.Journal.ByType(JournalEntryTypes.PassOut).Single().Payload.Should().Be("energy:6");

        // The deferred vigor crossing is drained by the next action, not the previous one.
        ActionResolver.Resolve(world, "wait");

        world.Clock.Should().Be(new GameClock(0, 20, 0));
        PassOutCount(world).Should().Be(2);
        world.Journal.ByType(JournalEntryTypes.PassOut)[1].Payload.Should().Be("vigor:6");
    }

    // ── Once per crossing / re-arm (AC 9) ────────────────────────────────────

    [Fact]
    public void PassOut_FiresOnce_StaysQuietWhileCritical_AndReFiresAfterRecovery()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            rules: new WorldRules(forcedSleepHours: 1),
            actions:
            [
                Act("exhaust", energyCost: 100m),
                Act("wait"),
                Act("nap", effect: new ActionEffect.StatDelta("energy", 50m)),
                Act("exhaust_again", energyCost: 100m),
            ]);

        ActionResolver.Resolve(world, "exhaust");
        PassOutCount(world).Should().Be(1);

        // Energy is pinned at 0 and the critical event stays disarmed.
        ActionResolver.Resolve(world, "wait");
        ActionResolver.Resolve(world, "wait");
        PassOutCount(world).Should().Be(1);

        // Recovering above the threshold re-arms the crossing.
        ActionResolver.Resolve(world, "nap");
        PassOutCount(world).Should().Be(1);
        world.Player.Stats.ValueOf("energy").Should().Be(50m);

        ActionResolver.Resolve(world, "exhaust_again");
        PassOutCount(world).Should().Be(2);
    }

    // ── Journal & determinism (AC 10, AC 11) ─────────────────────────────────

    [Fact]
    public void PassOut_JournalsSingleEntry_WithPassOutClockAndPayload()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        var entry = world.Journal.ByType(JournalEntryTypes.PassOut).Should().ContainSingle().Which;
        entry.SimTime.Should().Be(new GameClock(0, 8, 0));
        entry.Payload.Should().Be("energy:6");
        entry.IsPinned.Should().BeFalse();

        JournalTypes(world).Should().Equal(JournalEntryTypes.ActionResolved, JournalEntryTypes.PassOut);
    }

    [Fact]
    public void PassOut_WhenSleepRollsADay_JournalsDayStartedAfterPassOut()
    {
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            clock: new GameClock(0, 22, 0),
            actions: [Act("exhaust", energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        world.Clock.Should().Be(new GameClock(1, 4, 0));
        JournalTypes(world).Should().Equal(
            JournalEntryTypes.ActionResolved,
            JournalEntryTypes.PassOut,
            JournalEntryTypes.DayStarted);

        var dayStarted = world.Journal.ByType(JournalEntryTypes.DayStarted).Single();
        dayStarted.SimTime.Should().Be(new GameClock(1, 4, 0));
        dayStarted.Payload.Should().Be("1");
    }

    [Fact]
    public void PassOut_OrderIsActionResolved_DayStarted_ThenPassOut()
    {
        // The action itself rolls the day (22:30 -> 00:00), then a 6h sleep stays within day 1.
        var world = Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            clock: new GameClock(0, 22, 30),
            actions: [Act("exhaust", timeCost: 90, energyCost: 100m)]);

        ActionResolver.Resolve(world, "exhaust");

        JournalTypes(world).Should().Equal(
            JournalEntryTypes.ActionResolved,
            JournalEntryTypes.DayStarted,
            JournalEntryTypes.PassOut);

        var passOut = world.Journal.ByType(JournalEntryTypes.PassOut).Single();
        passOut.SimTime.Should().Be(new GameClock(1, 0, 0));
        world.Clock.Should().Be(new GameClock(1, 6, 0));
    }

    [Fact]
    public void PassOut_IdenticalRuns_ProduceIdenticalStateHash()
    {
        WorldState Run()
        {
            var world = Build(
                StandardDefs(),
                Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
                actions: [Act("exhaust", timeCost: 30, energyCost: 100m), Act("wait", timeCost: 60)]);
            ActionResolver.Resolve(world, "exhaust");
            ActionResolver.Resolve(world, "wait");
            return world;
        }

        var first = Run();
        var second = Run();

        PassOutCount(first).Should().BeGreaterThan(0, because: "the run must actually exercise the pass-out path");
        StateHasher.Compute(second).Should().Be(StateHasher.Compute(first));
    }

    [Fact]
    public void PassOut_Playthrough_ReplayReproducesStateHash()
    {
        const string exhaust = "exhaust";
        const string wait = "wait";

        WorldState Start() => Build(
            StandardDefs(),
            Initial(("energy", 100m), ("mood", 100m), ("focus", 100m)),
            actions: [Act(exhaust, timeCost: 30, energyCost: 100m), Act(wait, timeCost: 60)]);

        var original = Start();
        ActionResolver.Resolve(original, exhaust);
        ActionResolver.Resolve(original, wait);

        PassOutCount(original).Should().BeGreaterThan(0, because: "replay must reproduce a state that actually passed out");

        var replayed = Start();
        foreach (var entry in original.Journal.ByType(JournalEntryTypes.ActionResolved))
        {
            ActionResolver.Resolve(replayed, entry.Payload!).IsSuccess.Should().BeTrue();
        }

        // The forced sleep and mood penalty are re-derived from the re-applied actions, so the
        // PassOut journal entries need not be replayed to reproduce the state hash.
        StateHasher.Compute(replayed).Should().Be(StateHasher.Compute(original));
    }
}
