using FluentAssertions;
using LifeSim.Core.Stats;
using Xunit;

namespace LifeSim.Core.Tests;

public class StatSetTests
{
    private static StatSet Needs(Dictionary<string, decimal>? initial = null) => new(
        [
            new StatDef("energy", 0m, 100m, DecayPerHour: 2m, CriticalAt: 0m, Consequence: StatConsequence.PassOut),
            new StatDef("hunger", 0m, 100m, DecayPerHour: 3m, CriticalAt: 0m, Consequence: StatConsequence.Starve),
            new StatDef("health", 0m, 100m),
        ],
        initial);

    [Fact]
    public void ApplyHourPassed_DecaysEveryDecayingStat()
    {
        var set = Needs(new() { ["energy"] = 50m, ["hunger"] = 50m, ["health"] = 100m });

        set.ApplyHourPassed();

        set.ValueOf("energy").Should().Be(48m);
        set.ValueOf("hunger").Should().Be(47m);
        set.ValueOf("health").Should().Be(100m);
    }

    [Fact]
    public void StatCritical_FiresOncePerCrossing_NotEveryTick()
    {
        var set = Needs(new() { ["energy"] = 2m, ["hunger"] = 100m, ["health"] = 100m });
        var events = new List<string>();
        set.StatCritical += e => events.Add(e.StatId);

        set.ApplyHourPassed();
        set.ApplyHourPassed();
        set.ApplyHourPassed();

        events.Should().ContainSingle().Which.Should().Be("energy");
    }

    [Fact]
    public void PassOut_Consequence_IsReportedOnCrossing()
    {
        var set = Needs(new() { ["energy"] = 1m, ["hunger"] = 100m, ["health"] = 100m });
        StatCriticalEventArgs? captured = null;
        set.StatCritical += e => captured ??= e;

        set.ApplyHourPassed();

        captured.Should().NotBeNull();
        captured!.StatId.Should().Be("energy");
        captured.Consequence.Should().Be(StatConsequence.PassOut);
        captured.Value.Should().Be(0m);
    }

    [Fact]
    public void Starvation_DrainsHealth_WhileHungerIsCritical()
    {
        var set = Needs(new() { ["energy"] = 100m, ["hunger"] = 1m, ["health"] = 100m });

        set.ApplyHourPassed();
        set.ValueOf("hunger").Should().Be(0m);
        set.ValueOf("health").Should().Be(95m);

        set.ApplyHourPassed();
        set.ValueOf("health").Should().Be(90m);
    }

    [Fact]
    public void CriticalReArms_AfterRecovery_ThenFiresAgain()
    {
        var set = Needs(new() { ["energy"] = 2m, ["hunger"] = 100m, ["health"] = 100m });
        var events = 0;
        set.StatCritical += _ => events++;

        set.ApplyHourPassed();          // 2 -> 0, fire
        set.ApplyDelta("energy", 10m);  // 0 -> 10, recover, re-arm
        set.ApplyHourPassed();          // 10 -> 8, no fire
        events.Should().Be(1);

        set.ApplyDelta("energy", -100m); // 8 -> 0, cross again
        events.Should().Be(2);
    }

    [Fact]
    public void ApplyDelta_OnStatSet_FiresEventOnCrossing()
    {
        var set = Needs(new() { ["energy"] = 1m, ["hunger"] = 100m, ["health"] = 100m });
        var fired = 0;
        set.StatCritical += _ => fired++;

        set.ApplyDelta("energy", -5m);
        set.ApplyDelta("energy", -5m);

        fired.Should().Be(1);
    }

    [Fact]
    public void MissingInitialValue_DefaultsToMax()
    {
        var set = new StatSet([new StatDef("energy", 0m, 100m)]);

        set.ValueOf("energy").Should().Be(100m);
    }

    [Fact]
    public void UnknownStatId_Throws()
    {
        var set = Needs();
        var act = () => set.ApplyDelta("nope", 5m);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void DuplicateStatId_Throws()
    {
        var act = () => new StatSet(
        [
            new StatDef("energy", 0m, 100m),
            new StatDef("energy", 0m, 100m),
        ]);

        act.Should().Throw<ArgumentException>();
    }
}
