namespace LifeSim.Core.Time;

/// <summary>
/// Publishes <see cref="HourPassed"/> and <see cref="DayStarted"/> engine events as the
/// clock advances. <see cref="HourPassed"/> fires once per full hour elapsed;
/// <see cref="DayStarted"/> fires at most once per advance, regardless of how many days
/// were crossed. Consumers (stat decay, autosave, schedules) subscribe here.
/// </summary>
public sealed class GameClockEventDispatcher
{
    /// <summary>Raised once per full hour of simulated time elapsed during an advance.</summary>
    public event Action? HourPassed;

    /// <summary>Raised once per advance that increases the day index; the argument is the new day index.</summary>
    public event Action<int>? DayStarted;

    /// <summary>
    /// Advances <paramref name="current"/> by <paramref name="minutes"/>, publishing events
    /// for every hour passed and, when applicable, the day start, and returns the new clock.
    /// </summary>
    public GameClock Advance(GameClock current, int minutes)
    {
        var (next, hoursPassed, dayStarted) = current.Advance(minutes);

        for (var i = 0; i < hoursPassed; i++)
        {
            HourPassed?.Invoke();
        }

        if (dayStarted)
        {
            DayStarted?.Invoke(next.DayIndex);
        }

        return next;
    }
}
