using FluentAssertions;
using LifeSim.Core.Time;
using Xunit;

namespace LifeSim.Core.Tests;

public class GameClockEventDispatcherTests
{
    private readonly GameClockEventDispatcher _dispatcher = new();

    [Fact]
    public void Advance_OneHour_FiresHourPassedOnce_AndNoDayStarted()
    {
        var hourPassed = 0;
        var dayStarted = 0;
        _dispatcher.HourPassed += () => hourPassed++;
        _dispatcher.DayStarted += _ => dayStarted++;

        _dispatcher.Advance(new GameClock(0, 9, 0), 60);

        hourPassed.Should().Be(1);
        dayStarted.Should().Be(0);
    }

    [Fact]
    public void Advance_NinetyMinutes_CrossingTwoBoundaries_FiresHourPassedTwice()
    {
        var hourPassed = 0;
        _dispatcher.HourPassed += () => hourPassed++;

        _dispatcher.Advance(new GameClock(0, 13, 30), 90);

        // Changed 1 -> 2 (AGENTS rule 9): ADR-011 raises HourPassed once per HH:00 boundary crossed,
        // not once per elapsed `minutes / 60` chunk. 13:30 -> 15:00 crosses 14:00 and 15:00.
        hourPassed.Should().Be(2, because: "13:30→15:00 crosses the 14:00 and 15:00 boundaries (ADR-011)");
    }

    [Fact]
    public void Advance_ThreeHours_FiresHourPassedThreeTimes()
    {
        var hourPassed = 0;
        _dispatcher.HourPassed += () => hourPassed++;

        _dispatcher.Advance(new GameClock(0, 8, 0), 180);

        hourPassed.Should().Be(3);
    }

    [Fact]
    public void Advance_OneMinuteAcrossMidnight_FiresHourPassedOnce_AndDayStartedOnce()
    {
        var hourPassed = 0;
        var dayStarted = 0;
        _dispatcher.HourPassed += () => hourPassed++;
        _dispatcher.DayStarted += _ => dayStarted++;

        _dispatcher.Advance(new GameClock(0, 23, 59), 1);

        // Changed 0 -> 1 (AGENTS rule 9): ADR-011 ties HourPassed to HH:00 boundaries crossed, not
        // elapsed `minutes / 60` chunks. 23:59 -> 00:00 crosses the midnight boundary.
        hourPassed.Should().Be(1, because: "23:59→00:00 crosses the midnight boundary (ADR-011)");
        dayStarted.Should().Be(1);
    }

    [Fact]
    public void Advance_FortyEightHours_FiresDayStartedExactlyOnce_RegardlessOfAdvanceSize()
    {
        var hourPassed = 0;
        var dayStarted = 0;
        _dispatcher.HourPassed += () => hourPassed++;
        _dispatcher.DayStarted += _ => dayStarted++;

        _dispatcher.Advance(new GameClock(0, 0, 0), 48 * GameClock.MinutesPerHour);

        hourPassed.Should().Be(48);
        dayStarted.Should().Be(1);
    }

    [Fact]
    public void Advance_WithinDay_DoesNotFireDayStarted()
    {
        var dayStarted = 0;
        _dispatcher.DayStarted += _ => dayStarted++;

        _dispatcher.Advance(new GameClock(0, 8, 0), 600);

        dayStarted.Should().Be(0);
    }

    [Fact]
    public void Advance_ReportsNewDayIndex_OnDayStarted()
    {
        var reportedDay = -1;
        _dispatcher.DayStarted += day => reportedDay = day;

        _dispatcher.Advance(new GameClock(0, 23, 0), 60);

        reportedDay.Should().Be(1);
    }

    [Fact]
    public void Advance_NotifiesAllSubscribers()
    {
        var first = 0;
        var second = 0;
        _dispatcher.HourPassed += () => first++;
        _dispatcher.HourPassed += () => second++;

        _dispatcher.Advance(new GameClock(0, 0, 0), 60);

        first.Should().Be(1);
        second.Should().Be(1);
    }

    [Fact]
    public void Advance_ReturnsTheAdvancedClock()
    {
        // Updated for the ADR-011 return shape: the dispatcher returns the full advance result
        // (new clock, boundaries crossed, day started), not just the clock.
        var (next, boundariesCrossed, dayStarted) = _dispatcher.Advance(new GameClock(0, 23, 59), 1);

        next.Should().Be(new GameClock(1, 0, 0));
        boundariesCrossed.Should().Be(1);
        dayStarted.Should().BeTrue();
    }
}
