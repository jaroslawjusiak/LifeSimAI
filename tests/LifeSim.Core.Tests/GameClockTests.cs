using FluentAssertions;
using LifeSim.Core.Time;
using Xunit;

namespace LifeSim.Core.Tests;

public class GameClockTests
{
    // ── Construction & encapsulation ─────────────────────────────────────────

    [Fact]
    public void Constructor_ProducesExpectedDefaults()
    {
        var clock = new GameClock(0, 8, 30);

        clock.DayIndex.Should().Be(0);
        clock.Hour.Should().Be(8);
        clock.Minute.Should().Be(30);
        clock.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Constructor_RejectsInvalidHour(int hour)
    {
        var act = () => new GameClock(0, hour, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(60)]
    public void Constructor_RejectsInvalidMinute(int minute)
    {
        var act = () => new GameClock(0, 0, minute);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RejectsNegativeDayIndex()
    {
        var act = () => new GameClock(-1, 0, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GameClock_HasNoPublicSetters_SoItCannotBeMutatedInPlace()
    {
        var properties = typeof(GameClock).GetProperties();

        properties.Should().NotBeEmpty();
        properties.Should().OnlyContain(p => p.SetMethod == null,
            because: "the clock is immutable; only Advance (driven by the resolver) produces new values");
    }

    // ── Day-of-week derivation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, DayOfWeek.Monday)]
    [InlineData(1, DayOfWeek.Tuesday)]
    [InlineData(2, DayOfWeek.Wednesday)]
    [InlineData(3, DayOfWeek.Thursday)]
    [InlineData(4, DayOfWeek.Friday)]
    [InlineData(5, DayOfWeek.Saturday)]
    [InlineData(6, DayOfWeek.Sunday)]
    [InlineData(7, DayOfWeek.Monday)]
    [InlineData(14, DayOfWeek.Monday)]
    public void DayOfWeek_IsDerivedFromDayIndex(int dayIndex, DayOfWeek expected)
    {
        new GameClock(dayIndex, 0, 0).DayOfWeek.Should().Be(expected);
    }

    // ── Phase mapping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, DayPhase.Night)]
    [InlineData(5, DayPhase.Night)]
    [InlineData(6, DayPhase.Morning)]
    [InlineData(11, DayPhase.Morning)]
    [InlineData(12, DayPhase.Midday)]
    [InlineData(16, DayPhase.Midday)]
    [InlineData(17, DayPhase.Evening)]
    [InlineData(21, DayPhase.Evening)]
    [InlineData(22, DayPhase.Night)]
    [InlineData(23, DayPhase.Night)]
    public void Phase_MapsHourToDefaultSchedule(int hour, DayPhase expected)
    {
        new GameClock(0, hour, 0).Phase.Should().Be(expected);
    }

    // ── Advance ──────────────────────────────────────────────────────────────

    [Fact]
    public void Advance_ZeroMinutes_ReturnsEquivalentClock()
    {
        var clock = new GameClock(3, 14, 20);

        var (next, hoursPassed, dayStarted) = clock.Advance(0);

        next.Should().Be(clock);
        hoursPassed.Should().Be(0);
        dayStarted.Should().BeFalse();
    }

    [Fact]
    public void Advance_WithinHour_DoesNotIncrementHour()
    {
        var (next, hoursPassed, _) = new GameClock(0, 13, 30).Advance(29);

        next.Hour.Should().Be(13);
        next.Minute.Should().Be(59);
        hoursPassed.Should().Be(0);
    }

    [Fact]
    public void Advance_MultiHour_WithinDay_DoesNotRollDay()
    {
        var (next, hoursPassed, dayStarted) = new GameClock(0, 13, 30).Advance(90);

        next.Should().Be(new GameClock(0, 15, 0));
        hoursPassed.Should().Be(1);
        dayStarted.Should().BeFalse();
    }

    [Fact]
    public void Advance_AcrossMidnight_RollsToNextDay()
    {
        var (next, hoursPassed, dayStarted) = new GameClock(0, 23, 59).Advance(1);

        next.Should().Be(new GameClock(1, 0, 0));
        hoursPassed.Should().Be(0);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_MultiHour_AcrossMidnight_RollsDayAndHour()
    {
        var (next, hoursPassed, dayStarted) = new GameClock(0, 23, 0).Advance(120);

        next.Should().Be(new GameClock(1, 1, 0));
        hoursPassed.Should().Be(2);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_WeekRollover_PreservesDayOfWeek()
    {
        var (next, _, dayStarted) = new GameClock(0, 8, 0).Advance(7 * GameClock.MinutesPerDay);

        next.DayIndex.Should().Be(7);
        next.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Hour.Should().Be(8);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_MultipleDays_SetsCorrectState()
    {
        var (next, hoursPassed, dayStarted) = new GameClock(1, 0, 0).Advance((2 * GameClock.MinutesPerDay) + 90);

        next.Should().Be(new GameClock(3, 1, 30));
        hoursPassed.Should().Be(49);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_NegativeMinutes_Throws()
    {
        var act = () => new GameClock(0, 8, 0).Advance(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Derived minute helpers ───────────────────────────────────────────────

    [Fact]
    public void MinuteOfDay_And_TotalMinutes_AreConsistent()
    {
        var clock = new GameClock(2, 6, 45);

        clock.MinuteOfDay.Should().Be((6 * 60) + 45);
        clock.TotalMinutes.Should().Be((2 * GameClock.MinutesPerDay) + (6 * 60) + 45);
    }
}
