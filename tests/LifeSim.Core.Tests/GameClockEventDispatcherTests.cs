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
    public void Advance_NinetyMinutes_FiresHourPassedOnce()
    {
        var hourPassed = 0;
        _dispatcher.HourPassed += () => hourPassed++;

        _dispatcher.Advance(new GameClock(0, 13, 30), 90);

        hourPassed.Should().Be(1);
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
    public void Advance_OneMinuteAcrossMidnight_FiresDayStartedOnce_AndNoHourPassed()
    {
        var hourPassed = 0;
        var dayStarted = 0;
        _dispatcher.HourPassed += () => hourPassed++;
        _dispatcher.DayStarted += _ => dayStarted++;

        _dispatcher.Advance(new GameClock(0, 23, 59), 1);

        hourPassed.Should().Be(0);
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
        var next = _dispatcher.Advance(new GameClock(0, 23, 59), 1);

        next.Should().Be(new GameClock(1, 0, 0));
    }
}
