namespace LifeSim.Core.Stats;

/// <summary>
/// The aggregate of a player's (or NPC's) stats. Owns mutation: decay is applied here on
/// <see cref="ApplyHourPassed"/>, and every critical crossing is published once via
/// <see cref="StatCritical"/>. While a stat with the <see cref="StatConsequence.Starve"/>
/// consequence is critical, its configured target stat drains each hour.
/// </summary>
public sealed class StatSet
{
    private readonly Dictionary<string, Stat> _stats;
    private readonly decimal _starvationDrainPerHour;
    private readonly string _starvationDrainTarget;

    public StatSet(
        IEnumerable<StatDef> defs,
        IReadOnlyDictionary<string, decimal>? initialValues = null,
        decimal starvationDrainPerHour = -5m,
        string starvationDrainTarget = "health")
    {
        ArgumentNullException.ThrowIfNull(defs);
        if (starvationDrainPerHour > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(starvationDrainPerHour), "Starvation drain must be non-positive (it reduces the target).");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(starvationDrainTarget);

        _starvationDrainPerHour = starvationDrainPerHour;
        _starvationDrainTarget = starvationDrainTarget;

        _stats = [];
        foreach (var def in defs)
        {
            ArgumentNullException.ThrowIfNull(def);
            if (_stats.ContainsKey(def.Id))
            {
                throw new ArgumentException($"Duplicate stat id '{def.Id}'.", nameof(defs));
            }

            var initial = initialValues is not null && initialValues.TryGetValue(def.Id, out var value)
                ? value
                : def.Max;

            _stats.Add(def.Id, new Stat(def, initial));
        }
    }

    /// <summary>Raised once per downward critical crossing (never spammed on subsequent ticks).</summary>
    public event Action<StatCriticalEventArgs>? StatCritical;

    /// <summary>The stats in definition order.</summary>
    public IReadOnlyCollection<Stat> Stats => _stats.Values;

    /// <summary>Returns the stat with the given id, or null when absent.</summary>
    public Stat? Get(string id) => _stats.TryGetValue(id, out var stat) ? stat : null;

    /// <summary>Reads the current value of a stat, throwing when the id is unknown.</summary>
    public decimal ValueOf(string id) => Require(id).Value;

    /// <summary>Applies a delta to a stat, publishing <see cref="StatCritical"/> on crossing.</summary>
    public void ApplyDelta(string id, decimal delta)
    {
        var stat = Require(id);
        stat.ApplyDelta(delta, out var crossed);
        if (crossed)
        {
            StatCritical?.Invoke(new StatCriticalEventArgs(stat.Def.Id, stat.Def.Consequence, stat.Value));
        }
    }

    /// <summary>
    /// Applies one hour of decay to every stat, then applies the starvation consequence
    /// (drain the target stat) for any stat currently critical with the Starve consequence.
    /// </summary>
    public void ApplyHourPassed()
    {
        foreach (var stat in _stats.Values)
        {
            if (stat.Def.DecayPerHour != 0m)
            {
                stat.ApplyDelta(-stat.Def.DecayPerHour, out var crossed);
                if (crossed)
                {
                    StatCritical?.Invoke(new StatCriticalEventArgs(stat.Def.Id, stat.Def.Consequence, stat.Value));
                }
            }
        }

        foreach (var stat in _stats.Values)
        {
            if (stat.Def.Consequence == StatConsequence.Starve && stat.IsCritical && _stats.TryGetValue(_starvationDrainTarget, out var target))
            {
                target.ApplyDelta(_starvationDrainPerHour, out var crossed);
                if (crossed)
                {
                    StatCritical?.Invoke(new StatCriticalEventArgs(target.Def.Id, target.Def.Consequence, target.Value));
                }
            }
        }
    }

    private Stat Require(string id)
    {
        if (!_stats.TryGetValue(id, out var stat))
        {
            throw new KeyNotFoundException($"Unknown stat id '{id}'.");
        }

        return stat;
    }
}
