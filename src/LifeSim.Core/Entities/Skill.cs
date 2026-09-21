namespace LifeSim.Core.Entities;

/// <summary>A prerequisite another skill must satisfy (reached at least <see cref="MinLevel"/>).</summary>
public sealed record SkillPrerequisite(string SkillId, int MinLevel);

/// <summary>
/// The static definition of a skill: its name, its XP curve and its prerequisites.
/// <see cref="LevelUpXp"/> holds cumulative XP thresholds — reaching level <c>L</c> (L ≥ 2)
/// requires total XP of at least <c>LevelUpXp[L - 2]</c>; max level is <c>LevelUpXp.Count + 1</c>.
/// </summary>
public sealed record SkillDef(
    string Id,
    string Name,
    IReadOnlyList<int> LevelUpXp,
    IReadOnlyList<SkillPrerequisite> Prerequisites) : IEntityWithId;

/// <summary>A live skill instance: current XP and derived level. Starts at level 1.</summary>
public sealed class Skill
{
    public Skill(SkillDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        Def = def;
    }

    public SkillDef Def { get; }

    /// <summary>Total accumulated XP.</summary>
    public int Xp { get; private set; }

    /// <summary>Current level (starts at 1).</summary>
    public int Level { get; private set; } = 1;

    /// <summary>
    /// Adds XP and returns the levels gained (in ascending order). Levels only ever increase.
    /// </summary>
    public IReadOnlyList<int> AddXp(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "XP cannot be negative.");
        }

        Xp += amount;

        var gained = new List<int>();
        while (Level - 1 < Def.LevelUpXp.Count && Xp >= Def.LevelUpXp[Level - 1])
        {
            Level++;
            gained.Add(Level);
        }

        return gained;
    }
}

/// <summary>
/// The set of skills an entity knows, over a catalog of skill definitions. Level lookups and
/// prerequisite checks run against current levels; XP added to an unknown skill makes it known.
/// </summary>
public sealed class SkillSet
{
    private readonly IReadOnlyDictionary<string, SkillDef> _definitions;
    private readonly Dictionary<string, Skill> _known = [];

    public SkillSet(IEnumerable<SkillDef> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToDictionary(d => d.Id);
    }

    /// <summary>The skills currently known (at least level 1).</summary>
    public IReadOnlyCollection<Skill> Known => _known.Values;

    /// <summary>Returns a known skill, or null when it has not been trained yet.</summary>
    public Skill? Get(string id) => _known.TryGetValue(id, out var skill) ? skill : null;

    /// <summary>Whether the skill is known (has been trained to at least level 1).</summary>
    public bool IsKnown(string id) => _known.ContainsKey(id);

    /// <summary>Current level of a skill, or 0 when it is unknown.</summary>
    public int LevelOf(string id) => _known.TryGetValue(id, out var skill) ? skill.Level : 0;

    /// <summary>Adds XP to a skill (training it if unknown) and returns the levels gained.</summary>
    public IReadOnlyList<int> AddXp(string id, int amount)
    {
        if (!_definitions.TryGetValue(id, out var def))
        {
            throw new KeyNotFoundException($"Unknown skill id '{id}'.");
        }

        if (!_known.TryGetValue(id, out var skill))
        {
            skill = new Skill(def);
            _known[id] = skill;
        }

        return skill.AddXp(amount);
    }

    /// <summary>Whether every prerequisite of the skill is met at its current levels.</summary>
    public bool MeetsPrerequisites(string id)
    {
        if (!_definitions.TryGetValue(id, out var def))
        {
            throw new KeyNotFoundException($"Unknown skill id '{id}'.");
        }

        return def.Prerequisites.All(p => LevelOf(p.SkillId) >= p.MinLevel);
    }
}
