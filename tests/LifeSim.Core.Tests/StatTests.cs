using FluentAssertions;
using LifeSim.Core.Stats;
using Xunit;

namespace LifeSim.Core.Tests;

public class StatTests
{
    private static StatDef Energy => new("energy", 0m, 100m, 2m, 0m, StatConsequence.PassOut);

    private static StatDef Stress => new("stress", 0m, 100m, CriticalAt: 50m);

    [Fact]
    public void Constructor_ClampsInitialValue()
    {
        new Stat(Energy, 500m).Value.Should().Be(100m);
        new Stat(Energy, -500m).Value.Should().Be(0m);
    }

    [Fact]
    public void ApplyDelta_ClampsAtMax_OnHugePositiveDelta()
    {
        var stat = new Stat(Energy, 50m);

        stat.ApplyDelta(1_000_000m, out _);

        stat.Value.Should().Be(100m);
    }

    [Fact]
    public void ApplyDelta_ClampsAtMin_OnHugeNegativeDelta()
    {
        var stat = new Stat(Energy, 50m);

        stat.ApplyDelta(-1_000_000m, out _);

        stat.Value.Should().Be(0m);
    }

    [Fact]
    public void ApplyDelta_ReportsWhetherValueChanged()
    {
        var stat = new Stat(Energy, 100m);

        stat.ApplyDelta(10m, out _).Should().BeFalse();
        stat.ApplyDelta(-10m, out _).Should().BeTrue();
    }

    [Fact]
    public void Threshold_CrossesOnceDownward_AndRearmsAfterRecovery()
    {
        var stat = new Stat(Stress, 60m);

        stat.ApplyDelta(-15m, out var first); // 60 -> 45, crosses 50
        first.Should().BeTrue();
        stat.IsCriticalArmed.Should().BeFalse();
        stat.IsCritical.Should().BeTrue();

        stat.ApplyDelta(-10m, out var second); // 45 -> 35, still critical, disarmed
        second.Should().BeFalse();

        stat.ApplyDelta(30m, out var third); // 35 -> 65, recovers
        third.Should().BeFalse();
        stat.IsCriticalArmed.Should().BeTrue();
        stat.IsCritical.Should().BeFalse();

        stat.ApplyDelta(-20m, out var fourth); // 65 -> 45, crosses again
        fourth.Should().BeTrue();
    }

    [Fact]
    public void Stat_InitiallyCritical_IsDisarmed()
    {
        var stat = new Stat(Stress, 30m);

        stat.IsCritical.Should().BeTrue();
        stat.IsCriticalArmed.Should().BeFalse();
    }

    [Fact]
    public void IsCritical_IsTrueAtOrBelowThreshold()
    {
        new Stat(Stress, 50m).IsCritical.Should().BeTrue();
        new Stat(Stress, 51m).IsCritical.Should().BeFalse();
    }

    [Fact]
    public void NegativeMin_AllowsNegativeValues()
    {
        var stat = new Stat(new StatDef("money", -1000m, 1_000_000m), -500m);

        stat.Value.Should().Be(-500m);
    }

    [Fact]
    public void Constructor_RejectsMinGreaterThanMax()
    {
        var act = () => new Stat(new StatDef("bad", 100m, 0m), 50m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsCriticalAtOutsideRange()
    {
        var act = () => new Stat(new StatDef("bad", 0m, 100m, CriticalAt: 150m), 50m);

        act.Should().Throw<ArgumentException>();
    }
}
