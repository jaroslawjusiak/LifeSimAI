using System.Globalization;
using System.Text;
using FluentAssertions;
using LifeSim.Core.Actions;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;
using LifeSim.Core.Stats;
using LifeSim.Core.Time;
using Xunit;

namespace LifeSim.Core.Tests;

public class ActionResolverTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private static StatSet Stats() => new(
    [
        new StatDef("energy", 0m, 100m, DecayPerHour: 1m),
        new StatDef("hunger", 0m, 100m),
        new StatDef("health", 0m, 100m),
        new StatDef("mood", 0m, 100m),
    ]);

    private static SkillSet Skills() => new([new SkillDef("coding", "Coding", [100], [])]);

    private static Npc Mia() => new(
        "mia", "Mia",
        new StatSet([new StatDef("mood", 0m, 100m)]),
        new WeeklySchedule("cafe", []));

    private static Location Loc(string id, string? type = null) => new(id, id, [], type: type);

    private static WorldState BuildWorld(params ActionDefinition[] actions) =>
        BuildWorld(new GameClock(0, 9, 0), actions);

    private static WorldState BuildWorld(GameClock clock, params ActionDefinition[] actions) => new(
        new Player("Alex", Stats(), Skills(), 100m, "flat"),
        [Mia()],
        [Loc("flat", "home"), Loc("office", "workplace"), Loc("cafe")],
        [new Item("coffee", "Coffee")],
        [new SkillDef("coding", "Coding", [100], [])],
        actions,
        clock);

    private static ActionDefinition Act(
        string id,
        IReadOnlyList<ActionRequirement>? requirements = null,
        IReadOnlyList<ActionEffect>? effects = null,
        int timeCost = 0,
        decimal energyCost = 0m,
        decimal moneyCost = 0m) =>
        new(id, ActionVerb.Custom, "Test", timeCost, energyCost, moneyCost, requirements ?? [], effects ?? []);

    private static string Snapshot(WorldState world)
    {
        var sb = new StringBuilder();
        sb.Append("loc=").Append(world.Player.LocationId)
          .Append("|money=").Append(world.Player.Money.ToString(CultureInfo.InvariantCulture))
          .Append("|clock=").Append(world.Clock)
          .Append("|flags=").Append(string.Join(",", world.Flags.OrderBy(x => x, StringComparer.Ordinal)))
          .Append("|unlocks=").Append(string.Join(",", world.Unlocks.OrderBy(x => x, StringComparer.Ordinal)))
          .Append("|stats=");

        foreach (var stat in world.Player.Stats.Stats.OrderBy(x => x.Def.Id, StringComparer.Ordinal))
        {
            sb.Append(stat.Def.Id).Append(':').Append(stat.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        sb.Append("|skills=");
        foreach (var skill in world.Player.Skills.Known.OrderBy(x => x.Def.Id, StringComparer.Ordinal))
        {
            sb.Append(skill.Def.Id).Append(':').Append(skill.Level).Append(';');
        }

        sb.Append("|npcs=");
        foreach (var npc in world.Npcs.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            sb.Append(npc.Id).Append(':').Append(npc.Relationship.Value).Append(';');
        }

        return sb.ToString();
    }

    // ── Unknown ids ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_UnknownActionId_ReturnsReadableError()
    {
        var world = BuildWorld();

        var result = ActionResolver.Resolve(world, "does-not-exist");

        result.IsSuccess.Should().BeFalse();
        result.Reasons.Should().ContainSingle().Which.Should().Contain("does-not-exist");
    }

    [Fact]
    public void Resolve_MoveToUnknownTarget_ReturnsReadableError()
    {
        var world = BuildWorld(Act("go", effects: [new ActionEffect.Move()]));

        var result = ActionResolver.Resolve(world, "go", "nowhere");

        result.IsSuccess.Should().BeFalse();
        result.Reasons.Should().ContainSingle().Which.Should().Contain("nowhere");
    }

    // ── Requirement kinds ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_StatGte_PassesWhenSufficient()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.StatGte("energy", 50)]));

        ActionResolver.Resolve(world, "work").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_StatGte_FailsWithReasonWhenInsufficient()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.StatGte("energy", 200)]));

        var result = ActionResolver.Resolve(world, "work");

        result.IsSuccess.Should().BeFalse();
        result.Reasons.Should().ContainSingle(r => r.Contains("energy"));
    }

    [Fact]
    public void Resolve_SkillGte_FailsWhenLevelTooLow()
    {
        var world = BuildWorld(Act("code", [new ActionRequirement.SkillGte("coding", 2)]));

        var result = ActionResolver.Resolve(world, "code");

        result.IsSuccess.Should().BeFalse();
        result.Reasons.Should().ContainSingle(r => r.Contains("coding"));
    }

    [Fact]
    public void Resolve_LocationIs_FailsWhenElsewhere()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.LocationIs("office")]));

        ActionResolver.Resolve(world, "work").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Resolve_LocationType_PassesWhenTypeMatches()
    {
        var world = BuildWorld(Act("sleep", [new ActionRequirement.LocationType("home")]));

        ActionResolver.Resolve(world, "sleep").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_LocationType_FailsWhenTypeMismatches()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.LocationType("workplace")]));

        ActionResolver.Resolve(world, "work").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Resolve_RelationshipGte_FailsWhenTooLow()
    {
        var world = BuildWorld(Act("ask", [new ActionRequirement.RelationshipGte("mia", 10)]));

        ActionResolver.Resolve(world, "ask").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Resolve_HasItem_PassesWhenItemHeld()
    {
        var world = BuildWorld(Act("drink", [new ActionRequirement.HasItem("coffee")]));
        world.Player.AddItem("coffee");

        ActionResolver.Resolve(world, "drink").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_HasItem_FailsWhenItemMissing()
    {
        var world = BuildWorld(Act("drink", [new ActionRequirement.HasItem("coffee")]));

        ActionResolver.Resolve(world, "drink").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FlagSet_FailsWhenFlagAbsent()
    {
        var world = BuildWorld(Act("secret", [new ActionRequirement.FlagSet("met_mia")]));

        ActionResolver.Resolve(world, "secret").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FlagSet_PassesWhenFlagSet()
    {
        var world = BuildWorld(Act("secret", [new ActionRequirement.FlagSet("met_mia")]));
        world.Flags.Add("met_mia");

        ActionResolver.Resolve(world, "secret").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_TimeWindow_FailsOutsideWindow()
    {
        var world = BuildWorld(Act("late", [new ActionRequirement.TimeWindow(18, 20)]));

        ActionResolver.Resolve(world, "late").IsSuccess.Should().BeFalse();
    }

    // ── Effect kinds ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_StatDelta_Applies()
    {
        var world = BuildWorld(Act("eat", effects: [new ActionEffect.StatDelta("energy", 10m)]));

        ActionResolver.Resolve(world, "eat");

        world.Player.Stats.ValueOf("energy").Should().Be(100m); // clamped at max
    }

    [Fact]
    public void Resolve_Money_Applies()
    {
        var world = BuildWorld(Act("paid", effects: [new ActionEffect.Money(50m)]));

        ActionResolver.Resolve(world, "paid");

        world.Player.Money.Should().Be(150m);
    }

    [Fact]
    public void Resolve_SkillXp_Applies()
    {
        var world = BuildWorld(Act("study", effects: [new ActionEffect.SkillXp("coding", 100)]));

        ActionResolver.Resolve(world, "study");

        world.Player.Skills.LevelOf("coding").Should().Be(2);
    }

    [Fact]
    public void Resolve_Relationship_Applies()
    {
        var world = BuildWorld(Act("talk", effects: [new ActionEffect.Relationship("mia", 5)]));

        ActionResolver.Resolve(world, "talk");

        world.GetNpc("mia")!.Relationship.Value.Should().Be(5);
    }

    [Fact]
    public void Resolve_SetFlag_Applies()
    {
        var world = BuildWorld(Act("meet", effects: [new ActionEffect.SetFlag("met_mia")]));

        ActionResolver.Resolve(world, "meet");

        world.Flags.Should().Contain("met_mia");
    }

    [Fact]
    public void Resolve_Move_AppliesTargetLocation()
    {
        var world = BuildWorld(Act("go", effects: [new ActionEffect.Move()], timeCost: 30));

        ActionResolver.Resolve(world, "go", "office");

        world.Player.LocationId.Should().Be("office");
    }

    [Fact]
    public void Resolve_Unlock_Applies()
    {
        var world = BuildWorld(Act("gym", effects: [new ActionEffect.Unlock("gym-membership")]));

        ActionResolver.Resolve(world, "gym");

        world.Unlocks.Should().Contain("gym-membership");
    }

    // ── Failure journaling (D3) ──────────────────────────────────────────────

    [Fact]
    public void Resolve_FailedPrecondition_AppendsActionFailedEntry()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.LocationIs("office")]));

        var result = ActionResolver.Resolve(world, "work");

        result.IsSuccess.Should().BeFalse();
        var entry = world.Journal.ByType(JournalEntryTypes.ActionFailed).Should().ContainSingle().Which;
        entry.Payload.Should().Contain("work");
        entry.CorrelationId.Should().NotBeNull();
    }

    [Fact]
    public void Resolve_SuccessfulAction_DoesNotAppendActionFailedEntry()
    {
        var world = BuildWorld(Act("idle"));

        ActionResolver.Resolve(world, "idle");

        world.Journal.ByType(JournalEntryTypes.ActionFailed).Should().BeEmpty();
    }

    // ── Atomicity & snapshot equality ────────────────────────────────────────

    [Fact]
    public void Resolve_FailedPrecondition_MutatesNothing()
    {
        var world = BuildWorld(Act("work", [new ActionRequirement.LocationIs("office")], effects: [new ActionEffect.Money(100m)]));
        var before = Snapshot(world);

        var result = ActionResolver.Resolve(world, "work");

        result.IsSuccess.Should().BeFalse();
        Snapshot(world).Should().Be(before);
    }

    [Fact]
    public void Resolve_InvalidEffectReference_AppliesNothing()
    {
        var world = BuildWorld(Act(
            "broken",
            effects: [new ActionEffect.StatDelta("energy", -10m), new ActionEffect.SkillXp("unknown", 5)]));
        var before = Snapshot(world);

        var result = ActionResolver.Resolve(world, "broken");

        result.IsSuccess.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("unknown"));
        Snapshot(world).Should().Be(before);
    }

    // ── Costs & clock ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ChargesEnergyAndMoneyCost()
    {
        var world = BuildWorld(Act("work", energyCost: 15m, moneyCost: 20m));

        ActionResolver.Resolve(world, "work");

        world.Player.Stats.ValueOf("energy").Should().Be(85m);
        world.Player.Money.Should().Be(80m);
    }

    [Fact]
    public void Resolve_AdvancesClock_AndAppliesHourlyDecay()
    {
        var world = BuildWorld(Act("sleep", timeCost: 120));

        ActionResolver.Resolve(world, "sleep");

        world.Clock.Hour.Should().Be(11);
        world.Player.Stats.ValueOf("energy").Should().Be(98m); // 100 - 2 hours decay
    }

    [Theory]
    [InlineData(0, 0)]    // 14:30 -> 14:30, no boundary crossed
    [InlineData(29, 0)]   // 14:30 -> 14:59, no boundary crossed
    [InlineData(30, 1)]   // 14:30 -> 15:00, one boundary crossed (old chunk rule: zero)
    [InlineData(60, 1)]   // 14:30 -> 15:30, one boundary crossed
    [InlineData(90, 2)]   // 14:30 -> 16:00, two boundaries crossed (old chunk rule: one)
    [InlineData(120, 2)]  // 14:30 -> 16:30, two boundaries crossed
    public void Resolve_DecaysOncePerHourBoundaryCrossed(int timeCostMinutes, int expectedTicks)
    {
        // D12/ADR-011 closure proof: WorldState subscribes ApplyHourPassed to the dispatcher's
        // HourPassed once at construction, and the dispatcher raises HourPassed once per HH:00
        // boundary crossed. 'energy' decays 1 per tick, so this also proves the subscription is
        // wired exactly once (a duplicate would over-decay). The 14:30 start is deliberate: it is
        // off the hour, so the boundary rule disagrees with the old `minutes / 60` chunk rule.
        var world = BuildWorld(new GameClock(0, 14, 30), Act("wait", timeCost: timeCostMinutes));

        ActionResolver.Resolve(world, "wait");

        world.Player.Stats.ValueOf("energy").Should().Be(100m - expectedTicks);
    }

    [Fact]
    public void Resolve_ThirtyMinutePartition_DecaysSameAsSingleSixtyMinuteAction()
    {
        // D5 closure proof: two 30-minute actions (14:30 -> 15:00 -> 15:30) must decay exactly the
        // same total as one 60-minute action (14:30 -> 15:30). Under the old `minutes / 60` rule the
        // partition decayed zero times while the single action decayed once.
        var single = BuildWorld(new GameClock(0, 14, 30), Act("long", timeCost: 60));
        ActionResolver.Resolve(single, "long");

        var partitioned = BuildWorld(new GameClock(0, 14, 30), Act("short", timeCost: 30));
        ActionResolver.Resolve(partitioned, "short");
        ActionResolver.Resolve(partitioned, "short");

        partitioned.Clock.Should().Be(single.Clock);
        partitioned.Player.Stats.ValueOf("energy").Should().Be(99m);
        partitioned.Player.Stats.ValueOf("energy").Should().Be(single.Player.Stats.ValueOf("energy"));
    }
}
