using System.Globalization;
using System.Text;
using LifeSim.Core.Actions;
using LifeSim.Core.Journal;
using LifeSim.Core.Time;

namespace LifeSim.Core.Entities;

/// <summary>
/// The aggregate root of the in-memory world: the player plus id-keyed maps of NPCs,
/// locations, items, skills and actions, together with the clock, world flags and unlocks.
/// All lookups are by id, never by name.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<string, Npc> _npcs;
    private readonly Dictionary<string, Location> _locations;
    private readonly Dictionary<string, Item> _items;
    private readonly Dictionary<string, SkillDef> _skills;
    private readonly Dictionary<string, ActionDefinition> _actions;

    public WorldState(
        Player player,
        IEnumerable<Npc> npcs,
        IEnumerable<Location> locations,
        IEnumerable<Item> items,
        IEnumerable<SkillDef> skills,
        IEnumerable<ActionDefinition> actions,
        GameClock? initialClock = null)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));

        _npcs = Index(npcs, "NPC");
        _locations = Index(locations, "location");
        _items = Index(items, "item");
        _skills = Index(skills, "skill");
        _actions = Index(actions, "action");

        Clock = initialClock ?? new GameClock(0, 8, 0);
        Flags = [];
        Unlocks = [];
        Journal = new EventJournal();
    }

    public Player Player { get; }

    /// <summary>The current simulation clock. Advanced only by the action resolver.</summary>
    public GameClock Clock { get; private set; }

    /// <summary>World-level flags (set by actions, checked by FlagSet requirements).</summary>
    public HashSet<string> Flags { get; }

    /// <summary>World-level content unlocks (skills/locations/actions made available).</summary>
    public HashSet<string> Unlocks { get; }

    /// <summary>The append-only event journal.</summary>
    public EventJournal Journal { get; }

    public IReadOnlyCollection<Npc> Npcs => _npcs.Values;

    public IReadOnlyCollection<Location> Locations => _locations.Values;

    public IReadOnlyCollection<Item> Items => _items.Values;

    public IReadOnlyCollection<SkillDef> Skills => _skills.Values;

    public IReadOnlyCollection<ActionDefinition> Actions => _actions.Values;

    public Npc? GetNpc(string id) => _npcs.TryGetValue(id, out var v) ? v : null;

    public Location? GetLocation(string id) => _locations.TryGetValue(id, out var v) ? v : null;

    public Item? GetItem(string id) => _items.TryGetValue(id, out var v) ? v : null;

    public SkillDef? GetSkill(string id) => _skills.TryGetValue(id, out var v) ? v : null;

    public ActionDefinition? GetAction(string id) => _actions.TryGetValue(id, out var v) ? v : null;

    /// <summary>Advances the clock forward and reports the hours passed and whether a day started.</summary>
    internal (int HoursPassed, bool DayStarted) AdvanceClock(int minutes)
    {
        var (next, hoursPassed, dayStarted) = Clock.Advance(minutes);
        Clock = next;
        return (hoursPassed, dayStarted);
    }

    /// <summary>
    /// Produces a canonical, deterministic string of the world's live state (clock, player,
    /// NPCs, flags, unlocks). The journal is deliberately excluded — it is history, not state.
    /// </summary>
    public string CreateSnapshot()
    {
        var sb = new StringBuilder();
        sb.Append("clock=").Append(Clock).Append(';');
        sb.Append("player=")
          .Append(Player.Name).Append('|')
          .Append(Player.LocationId).Append('|')
          .Append(Player.Money.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(string.Join(",", Player.Traits.OrderBy(t => t, StringComparer.Ordinal))).Append('|')
          .Append(string.Join(",", Player.Inventory.OrderBy(i => i.ItemId, StringComparer.Ordinal).Select(i => $"{i.ItemId}x{i.Quantity}"))).Append(';');

        sb.Append("stats=");
        foreach (var stat in Player.Stats.Stats.OrderBy(s => s.Def.Id, StringComparer.Ordinal))
        {
            sb.Append(stat.Def.Id).Append(':').Append(stat.Value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        sb.Append(";skills=");
        foreach (var skill in Player.Skills.Known.OrderBy(s => s.Def.Id, StringComparer.Ordinal))
        {
            sb.Append(skill.Def.Id).Append(':').Append(skill.Level).Append(',').Append(skill.Xp).Append('|');
        }

        sb.Append(";npcs=");
        foreach (var npc in Npcs.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            sb.Append(npc.Id).Append('{')
              .Append("mood=").Append(npc.Mood).Append('|')
              .Append("rel=").Append(npc.Relationship.Value).Append('|')
              .Append("flags=").Append(string.Join(",", npc.Flags.OrderBy(f => f, StringComparer.Ordinal))).Append('|')
              .Append("stats=");
            foreach (var stat in npc.Stats.Stats.OrderBy(s => s.Def.Id, StringComparer.Ordinal))
            {
                sb.Append(stat.Def.Id).Append(':').Append(stat.Value.ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            sb.Append('}');
        }

        sb.Append(";flags=").Append(string.Join(",", Flags.OrderBy(f => f, StringComparer.Ordinal)));
        sb.Append(";unlocks=").Append(string.Join(",", Unlocks.OrderBy(u => u, StringComparer.Ordinal)));

        return sb.ToString();
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> values, string kind)
        where T : IEntityWithId
    {
        ArgumentNullException.ThrowIfNull(values);

        var map = new Dictionary<string, T>();
        foreach (var value in values)
        {
            if (!map.TryAdd(value.Id, value))
            {
                throw new ArgumentException($"Duplicate {kind} id '{value.Id}'.");
            }
        }

        return map;
    }
}

/// <summary>Contract for entities addressable by id.</summary>
public interface IEntityWithId
{
    string Id { get; }
}
