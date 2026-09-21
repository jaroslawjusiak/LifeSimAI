namespace LifeSim.Core.Stats;

/// <summary>
/// Payload for the <see cref="StatSet.StatCritical"/> event: which stat crossed, what
/// consequence it carries, and its (clamped) value at the moment of crossing.
/// </summary>
public sealed record StatCriticalEventArgs(
    string StatId,
    StatConsequence Consequence,
    decimal Value);
