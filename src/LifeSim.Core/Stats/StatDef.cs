namespace LifeSim.Core.Stats;

/// <summary>
/// The world-rule definition of a stat: its clamped range, per-hour decay, critical
/// threshold and the consequence of crossing it. Values are <see cref="decimal"/> so needs
/// (typically 0–100) and money (with debt via a negative <see cref="Min"/>) share one model.
/// </summary>
public sealed record StatDef(
    string Id,
    decimal Min,
    decimal Max,
    decimal DecayPerHour = 0m,
    decimal? CriticalAt = null,
    StatConsequence Consequence = StatConsequence.None);
