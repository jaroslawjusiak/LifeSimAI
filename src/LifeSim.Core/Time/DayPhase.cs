namespace LifeSim.Core.Time;

/// <summary>
/// The broad phase of a 24-hour day, used for schedules, opening hours and narration tone.
/// Boundaries follow the default world schedule (Night 00–05 & 22–23, Morning 06–11,
/// Midday 12–16, Evening 17–21); per-world phase configuration arrives with world rules.
/// </summary>
public enum DayPhase
{
    Night,
    Morning,
    Midday,
    Evening,
}
