namespace LifeSim.Core.Entities;

/// <summary>
/// A single time block in an NPC's weekly schedule: a set of weekdays, a half-open hour
/// window and the location to be at. An overnight range (start ≥ end) wraps within the day,
/// covering <c>[start, 24) ∪ [0, end)</c>.
/// </summary>
public sealed record ScheduleEntry(
    IReadOnlySet<DayOfWeek> Days,
    int StartHour,
    int EndHour,
    string LocationId);

/// <summary>
/// An NPC's weekly routine. <see cref="Resolve"/> returns a location for every
/// <c>(day, hour)</c>, falling back to <see cref="HomeLocationId"/> when no entry matches.
/// </summary>
public sealed class WeeklySchedule
{
    private readonly List<ScheduleEntry> _entries;

    public WeeklySchedule(string homeLocationId, IEnumerable<ScheduleEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeLocationId);
        ArgumentNullException.ThrowIfNull(entries);

        _entries = [];
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.StartHour is < 0 or > 23 || entry.EndHour is < 0 or > 23)
            {
                throw new ArgumentOutOfRangeException(nameof(entry), "Schedule hours must be within 0–23.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(entry.LocationId);
            _entries.Add(entry);
        }

        HomeLocationId = homeLocationId;
    }

    /// <summary>The location an NPC defaults to when no entry matches.</summary>
    public string HomeLocationId { get; }

    /// <summary>The schedule entries, in precedence order (first match wins).</summary>
    public IReadOnlyList<ScheduleEntry> Entries => _entries;

    /// <summary>
    /// Returns the location id for the given weekday and hour, or <see cref="HomeLocationId"/>
    /// when no entry covers that time.
    /// </summary>
    public string Resolve(DayOfWeek day, int hour)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "Hour must be within 0–23.");
        }

        foreach (var entry in _entries)
        {
            if (entry.Days.Contains(day) && IsWithin(entry, hour))
            {
                return entry.LocationId;
            }
        }

        return HomeLocationId;
    }

    private static bool IsWithin(ScheduleEntry entry, int hour) =>
        entry.StartHour < entry.EndHour
            ? hour >= entry.StartHour && hour < entry.EndHour
            : hour >= entry.StartHour || hour < entry.EndHour;
}
