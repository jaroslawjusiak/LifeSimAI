using FluentAssertions;
using LifeSim.Core.Entities;
using Xunit;

namespace LifeSim.Core.Tests;

public class WeeklyScheduleTests
{
    private static readonly IReadOnlySet<DayOfWeek> Weekdays =
        new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday,
        };

    private static WeeklySchedule WorkSchedule() => new(
        "apartment",
        [new ScheduleEntry(Weekdays, 9, 17, "office")]);

    [Fact]
    public void Resolve_ReturnsLocation_WhenEntryMatches()
    {
        WorkSchedule().Resolve(DayOfWeek.Monday, 12).Should().Be("office");
    }

    [Fact]
    public void Resolve_FallsBackToHome_WhenNoEntryMatches()
    {
        WorkSchedule().Resolve(DayOfWeek.Monday, 20).Should().Be("apartment");
        WorkSchedule().Resolve(DayOfWeek.Saturday, 12).Should().Be("apartment");
    }

    [Fact]
    public void Resolve_ReturnsALocation_ForEveryDayAndHour()
    {
        var schedule = WorkSchedule();

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            for (var hour = 0; hour < 24; hour++)
            {
                schedule.Resolve(day, hour).Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public void Resolve_HandlesOvernightRange()
    {
        var schedule = new WeeklySchedule("home", [new ScheduleEntry(Weekdays, 22, 2, "night-shift")]);

        schedule.Resolve(DayOfWeek.Monday, 23).Should().Be("night-shift");
        schedule.Resolve(DayOfWeek.Monday, 1).Should().Be("night-shift");
        schedule.Resolve(DayOfWeek.Monday, 12).Should().Be("home");
    }

    [Fact]
    public void Resolve_FirstMatchWins_OnOverlappingEntries()
    {
        var schedule = new WeeklySchedule("home",
        [
            new ScheduleEntry(Weekdays, 9, 17, "office"),
            new ScheduleEntry(Weekdays, 12, 13, "cafe"),
        ]);

        schedule.Resolve(DayOfWeek.Monday, 12).Should().Be("office");
    }

    [Fact]
    public void Constructor_RejectsOutOfRangeHours()
    {
        var act = () => new WeeklySchedule("home", [new ScheduleEntry(Weekdays, 9, 24, "x")]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
