namespace LifeSim.Core.Entities;

/// <summary>
/// Payload for <see cref="Relationship.MilestoneReached"/>: which milestone threshold was
/// crossed and the relationship value at that moment.
/// </summary>
public sealed record RelationshipMilestoneEventArgs(int Milestone, int Value);

/// <summary>
/// A relationship value clamped to <c>[-100, 100]</c>. Milestone crossings at ±25, ±50 and
/// ±75 fire exactly once per crossing (never spammed while the value stays within a tier).
/// </summary>
public sealed class Relationship
{
    /// <summary>Minimum relationship value.</summary>
    public const int MinValue = -100;

    /// <summary>Maximum relationship value.</summary>
    public const int MaxValue = 100;

    /// <summary>Milestone thresholds (ordered by increasing magnitude for stable event ordering).</summary>
    public static readonly int[] Milestones = [25, 50, 75, -25, -50, -75];

    private int _value;

    public Relationship(int initialValue = 0)
    {
        _value = Math.Clamp(initialValue, MinValue, MaxValue);
    }

    /// <summary>The current (always in-range) value.</summary>
    public int Value => _value;

    /// <summary>Raised once for each milestone threshold crossed by an <see cref="ApplyDelta"/> call.</summary>
    public event Action<RelationshipMilestoneEventArgs>? MilestoneReached;

    /// <summary>
    /// Applies a delta, clamping to <c>[-100, 100]</c>, and raises
    /// <see cref="MilestoneReached"/> for every milestone threshold crossed.
    /// </summary>
    public void ApplyDelta(int delta)
    {
        var before = _value;
        _value = Math.Clamp(_value + delta, MinValue, MaxValue);

        foreach (var milestone in Milestones)
        {
            var wasAbove = before > milestone;
            var isAbove = _value > milestone;

            if (wasAbove != isAbove)
            {
                MilestoneReached?.Invoke(new RelationshipMilestoneEventArgs(milestone, _value));
            }
        }
    }
}
