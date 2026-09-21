using LifeSim.Core.Stats;

namespace LifeSim.Core.Entities;

/// <summary>
/// A non-player character: a stat subset, a current mood, a weekly schedule, a relationship
/// to the player, and arbitrary flags.
/// </summary>
public sealed class Npc : IEntityWithId
{
    public Npc(
        string id,
        string name,
        StatSet stats,
        WeeklySchedule schedule,
        string mood = "neutral",
        int relationship = 0,
        IEnumerable<string>? flags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(schedule);

        Id = id;
        Name = name;
        Stats = stats;
        Schedule = schedule;
        Mood = mood;
        Relationship = new Relationship(relationship);
        Flags = flags?.ToHashSet() ?? [];
    }

    public string Id { get; }

    public string Name { get; }

    public StatSet Stats { get; }

    public WeeklySchedule Schedule { get; }

    /// <summary>Current mood label (e.g. "neutral", "happy", "angry").</summary>
    public string Mood { get; set; }

    public Relationship Relationship { get; }

    /// <summary>Arbitrary world flags set on this NPC.</summary>
    public HashSet<string> Flags { get; }

    /// <summary>Resolves the NPC's location for a given weekday and hour.</summary>
    public string LocationAt(DayOfWeek day, int hour) => Schedule.Resolve(day, hour);
}
