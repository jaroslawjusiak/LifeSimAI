namespace LifeSim.Core.Time;

/// <summary>
/// The authoritative simulation clock: day index, derived weekday, hour and minute.
/// Immutable by design — the only way to reach a later point in time is
/// <see cref="Advance"/>, which the action resolver (and only the resolver) calls.
/// </summary>
public readonly record struct GameClock
{
    /// <summary>Minutes in one simulated hour.</summary>
    public const int MinutesPerHour = 60;

    /// <summary>Hours in one simulated day.</summary>
    public const int HoursPerDay = 24;

    /// <summary>Minutes in one simulated day.</summary>
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay;

    /// <summary>Weekday of day index zero. Day 0 is a Monday (start of the work week).</summary>
    public static readonly DayOfWeek StartingDayOfWeek = DayOfWeek.Monday;

    /// <summary>Zero-based day count since the start of the run.</summary>
    public int DayIndex { get; }

    /// <summary>Weekday derived from <see cref="DayIndex"/>.</summary>
    public DayOfWeek DayOfWeek { get; }

    /// <summary>Hour of the day, 0–23.</summary>
    public int Hour { get; }

    /// <summary>Minute of the hour, 0–59.</summary>
    public int Minute { get; }

    public GameClock(int dayIndex, int hour, int minute)
    {
        if (dayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dayIndex), dayIndex, "Day index cannot be negative.");
        }

        if (hour is < 0 or >= HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, $"Hour must be between 0 and {HoursPerDay - 1}.");
        }

        if (minute is < 0 or >= MinutesPerHour)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, $"Minute must be between 0 and {MinutesPerHour - 1}.");
        }

        DayIndex = dayIndex;
        Hour = hour;
        Minute = minute;
        DayOfWeek = (DayOfWeek)(((dayIndex % 7) + (int)StartingDayOfWeek) % 7);
    }

    /// <summary>Minutes elapsed since midnight of the current day.</summary>
    public int MinuteOfDay => Hour * MinutesPerHour + Minute;

    /// <summary>Minutes elapsed since day zero midnight.</summary>
    public int TotalMinutes => DayIndex * MinutesPerDay + MinuteOfDay;

    /// <summary>The phase of the current hour under the default schedule.</summary>
    public DayPhase Phase => PhaseFor(Hour);

    /// <summary>Resolves the default day phase for a given hour (0–23).</summary>
    public static DayPhase PhaseFor(int hour) => hour switch
    {
        >= 6 and < 12 => DayPhase.Morning,
        >= 12 and < 17 => DayPhase.Midday,
        >= 17 and < 22 => DayPhase.Evening,
        _ => DayPhase.Night,
    };

    /// <summary>
    /// Advances the clock forward by <paramref name="minutes"/>, rolling hour, day and week
    /// boundaries as needed.
    /// </summary>
    /// <returns>
    /// The new clock, the number of <em>hour boundaries</em> crossed, and whether the day index
    /// increased. A boundary is a clock hour mark (HH:00), so the count is
    /// <c>floor(newTotalMinutes / 60) − floor(TotalMinutes / 60)</c> — <em>not</em> the elapsed
    /// <c>minutes / 60</c> chunks. It telescopes over any partition of an interval: two
    /// consecutive advances cross exactly as many boundaries as one advance covering both, so
    /// the value is a pure, stateless function of <c>(start, minutes)</c> (ADR-011).
    /// </returns>
    public (GameClock NewClock, int BoundariesCrossed, bool DayStarted) Advance(int minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "The clock only advances forward.");
        }

        var newTotalMinutes = TotalMinutes + minutes;
        var newDayIndex = newTotalMinutes / MinutesPerDay;
        var minuteOfDay = newTotalMinutes % MinutesPerDay;
        var newHour = minuteOfDay / MinutesPerHour;
        var newMinute = minuteOfDay % MinutesPerHour;

        var boundariesCrossed = (newTotalMinutes / MinutesPerHour) - (TotalMinutes / MinutesPerHour);
        var dayStarted = newDayIndex > DayIndex;

        return (new GameClock(newDayIndex, newHour, newMinute), boundariesCrossed, dayStarted);
    }
}
