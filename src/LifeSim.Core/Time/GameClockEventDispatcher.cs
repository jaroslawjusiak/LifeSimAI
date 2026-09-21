namespace LifeSim.Core.Time;

/// <summary>
/// Owns the clock advance and publishes <see cref="HourPassed"/> and <see cref="DayStarted"/>
/// engine events. <see cref="HourPassed"/> fires once per <em>hour boundary</em> (HH:00)
/// crossed — not once per elapsed hour chunk — so the tick count telescopes over any partition
/// of an interval (ADR-011); <see cref="DayStarted"/> fires at most once per advance, regardless
/// of how many days were crossed. Consumers (stat decay, autosave, schedules) subscribe here.
/// </summary>
public sealed class GameClockEventDispatcher
{
    /// <summary>Raised once per hour boundary (HH:00) crossed during an advance.</summary>
    public event Action? HourPassed;

    /// <summary>Raised once per advance that increases the day index; the argument is the new day index.</summary>
    public event Action<int>? DayStarted;

    /// <summary>
    /// Advances <paramref name="current"/> by <paramref name="minutes"/> — the single entry point
    /// for simulation clock movement. Publishes <see cref="HourPassed"/> once per boundary crossed
    /// and, when the day index increased, <see cref="DayStarted"/> once, then returns the advance
    /// result: the new clock, the boundaries crossed, and whether the day started (ADR-011).
    /// </summary>
    public (GameClock NewClock, int BoundariesCrossed, bool DayStarted) Advance(GameClock current, int minutes)
    {
        var result = current.Advance(minutes);

        for (var i = 0; i < result.BoundariesCrossed; i++)
        {
            HourPassed?.Invoke();
        }

        if (result.DayStarted)
        {
            DayStarted?.Invoke(result.NewClock.DayIndex);
        }

        return result;
    }
}
