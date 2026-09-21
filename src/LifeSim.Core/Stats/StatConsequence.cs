namespace LifeSim.Core.Stats;

/// <summary>
/// The engine consequence attached to a stat crossing its critical threshold.
/// </summary>
public enum StatConsequence
{
    /// <summary>No special consequence; the crossing is still reported as an event.</summary>
    None,

    /// <summary>The entity passes out and is forced to sleep (energy-style exhaustion).</summary>
    PassOut,

    /// <summary>The entity is starving; a target stat (health) drains each hour while critical.</summary>
    Starve,
}
