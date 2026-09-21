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

        var (next, boundariesCrossed, dayStarted) = clock.Advance(0);

        next.Should().Be(clock);
        boundariesCrossed.Should().Be(0);
        dayStarted.Should().BeFalse();
    }

    [Fact]
    public void Advance_WithinHour_DoesNotIncrementHour()
    {
        var (next, boundariesCrossed, _) = new GameClock(0, 13, 30).Advance(29);

        next.Hour.Should().Be(13);
        next.Minute.Should().Be(59);
        boundariesCrossed.Should().Be(0);
    }

    [Fact]
    public void Advance_MultiHour_WithinDay_DoesNotRollDay()
    {
        var (next, boundariesCrossed, dayStarted) = new GameClock(0, 13, 30).Advance(90);

        next.Should().Be(new GameClock(0, 15, 0));
        // Changed 1 -> 2 (AGENTS rule 9): ADR-011 replaces the partition-dependent `minutes / 60`
        // chunk count with one decay tick per HH:00 boundary crossed. 13:30 -> 15:00 crosses the
        // 14:00 and 15:00 boundaries, so exactly two boundaries are crossed (the old value of 1
        // encoded the chunk rule the ruling declares defective).
        boundariesCrossed.Should().Be(2, because: "13:30→15:00 crosses the 14:00 and 15:00 boundaries (ADR-011)");
        dayStarted.Should().BeFalse();
    }

    [Fact]
    public void Advance_AcrossMidnight_RollsToNextDay()
    {
        var (next, boundariesCrossed, dayStarted) = new GameClock(0, 23, 59).Advance(1);

        next.Should().Be(new GameClock(1, 0, 0));
        // Changed 0 -> 1 (AGENTS rule 9): ADR-011 counts HH:00 boundaries crossed, not elapsed
        // whole `minutes / 60` chunks. 23:59 -> 00:00 crosses the midnight boundary, so one tick is
        // due even though only a single minute elapsed (the old value of 0 encoded the chunk rule).
        boundariesCrossed.Should().Be(1, because: "23:59→00:00 crosses the midnight boundary (ADR-011)");
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_MultiHour_AcrossMidnight_RollsDayAndHour()
    {
        var (next, boundariesCrossed, dayStarted) = new GameClock(0, 23, 0).Advance(120);

        next.Should().Be(new GameClock(1, 1, 0));
        boundariesCrossed.Should().Be(2);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_WeekRollover_PreservesDayOfWeek()
    {
        var (next, boundariesCrossed, dayStarted) = new GameClock(0, 8, 0).Advance(7 * GameClock.MinutesPerDay);

        next.DayIndex.Should().Be(7);
        next.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Hour.Should().Be(8);
        boundariesCrossed.Should().Be(168);
        dayStarted.Should().BeTrue();
    }

    [Fact]
    public void Advance_MultipleDays_SetsCorrectState()
    {
        var (next, boundariesCrossed, dayStarted) = new GameClock(1, 0, 0).Advance((2 * GameClock.MinutesPerDay) + 90);

        next.Should().Be(new GameClock(3, 1, 30));
        boundariesCrossed.Should().Be(49);
        dayStarted.Should().BeTrue();
    }

    // ── Boundary counting (ADR-011) ──────────────────────────────────────────

    [Fact]
    public void Advance_ThirtyMinutesTwice_CrossesSameBoundaryAsSixtyMinutesOnce()
    {
        // ADR-011: boundary counting is stateless and telescopes over any partition of an interval,
        // so splitting an hour across two actions must cross exactly the same number of boundaries
        // as one action covering the whole hour. Old chunk semantics gave 0 for the partition.
        var start = new GameClock(0, 14, 30);

        var (single, singleBoundaries, _) = start.Advance(60);
        var (at1500, firstBoundaries, _) = start.Advance(30);
        var (partitioned, secondBoundaries, _) = at1500.Advance(30);

        singleBoundaries.Should().Be(1);
        firstBoundaries.Should().Be(1);   // 14:30 -> 15:00 lands on the 15:00 boundary
        secondBoundaries.Should().Be(0);  // 15:00 -> 15:30 crosses no boundary
        (firstBoundaries + secondBoundaries).Should().Be(singleBoundaries);
        at1500.Should().Be(new GameClock(0, 15, 0));
        partitioned.Should().Be(single);
    }

    [Theory]
    [InlineData(0, 14, 30, 60, 1, 59)]   // boundary lands inside the second chunk
    [InlineData(0, 14, 30, 60, 29, 31)]
    [InlineData(0, 14, 30, 60, 30, 30)]  // boundary lands exactly on the split
    [InlineData(0, 13, 30, 90, 30, 60)]
    [InlineData(0, 23, 30, 90, 45, 45)]  // partition straddles midnight
    [InlineData(3, 8, 0, 300, 100, 200)]
    [InlineData(0, 8, 45, 1440, 15, 1425)]
    public void Advance_TwoChunkPartition_CrossesSameBoundariesAsSingleAdvance(
        int day, int hour, int minute, int totalMinutes, int firstChunk, int secondChunk)
    {
        var start = new GameClock(day, hour, minute);

        var (_, singleBoundaries, _) = start.Advance(totalMinutes);
        var (mid, firstBoundaries, _) = start.Advance(firstChunk);
        var (_, secondBoundaries, _) = mid.Advance(secondChunk);

        (firstBoundaries + secondBoundaries).Should().Be(singleBoundaries);
    }

    [Fact]
    public void Advance_TwentyFourHours_CrossesTwentyFourBoundaries()
    {
        var (_, boundariesCrossed, _) = new GameClock(0, 8, 0).Advance(GameClock.MinutesPerDay);

        boundariesCrossed.Should().Be(24);
    }

    [Fact]
    public void Advance_ThirtyDailyAdvances_CrossesSevenHundredTwentyBoundaries()
    {
        var clock = new GameClock(0, 8, 0);
        var total = 0;

        for (var day = 0; day < 30; day++)
        {
            var (next, boundariesCrossed, _) = clock.Advance(GameClock.MinutesPerDay);
            clock = next;
            total += boundariesCrossed;
        }

        total.Should().Be(720);
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
