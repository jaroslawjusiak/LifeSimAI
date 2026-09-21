namespace LifeSim.Core.Stats;

/// <summary>
/// A live stat value bound to a <see cref="StatDef"/>. The value is always clamped to
/// <c>[Min, Max]</c>. The critical threshold fires exactly once per downward crossing:
/// it disarms on crossing and re-arms only after the value recovers back above the threshold.
/// </summary>
public sealed class Stat
{
    private readonly decimal _min;
    private readonly decimal _max;

    public Stat(StatDef def, decimal initialValue)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def.Min > def.Max)
        {
            throw new ArgumentException($"Stat '{def.Id}' has Min ({def.Min}) greater than Max ({def.Max}).", nameof(def));
        }

        if (def.CriticalAt is { } threshold && (threshold < def.Min || threshold > def.Max))
        {
            throw new ArgumentException($"Stat '{def.Id}' has CriticalAt ({threshold}) outside [{def.Min}, {def.Max}].", nameof(def));
        }

        Def = def;
        _min = def.Min;
        _max = def.Max;
        Value = Clamp(initialValue);
        IsCriticalArmed = def.CriticalAt is null || Value > def.CriticalAt;
    }

    public StatDef Def { get; }

    /// <summary>The current (always in-range) value.</summary>
    public decimal Value { get; private set; }

    /// <summary>
    /// True when the stat is ready to fire on the next downward crossing. Disarmed after a
    /// crossing and re-armed only once the value recovers above <see cref="StatDef.CriticalAt"/>.
    /// </summary>
    public bool IsCriticalArmed { get; private set; }

    /// <summary>True when the value is currently at or below the critical threshold.</summary>
    public bool IsCritical => Def.CriticalAt is { } threshold && Value <= threshold;

    /// <summary>
    /// Applies a delta, clamping the result to <c>[Min, Max]</c>. Reports whether the value
    /// changed and whether a critical threshold was crossed (downward, while armed).
    /// </summary>
    public bool ApplyDelta(decimal delta, out bool thresholdCrossed)
    {
        thresholdCrossed = false;
        var previous = Value;
        Value = Clamp(Value + delta);

        if (Def.CriticalAt is { } threshold)
        {
            if (IsCriticalArmed && Value <= threshold)
            {
                thresholdCrossed = true;
                IsCriticalArmed = false;
            }
            else if (!IsCriticalArmed && Value > threshold)
            {
                IsCriticalArmed = true;
            }
        }

        return Value != previous;
    }

    private decimal Clamp(decimal value) => Math.Clamp(value, _min, _max);
}
