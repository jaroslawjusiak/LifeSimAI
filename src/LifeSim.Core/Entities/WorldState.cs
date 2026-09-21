using System.Globalization;
using System.Text;
using LifeSim.Core.Actions;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Journal;
using LifeSim.Core.Rules;
using LifeSim.Core.Stats;
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
    private readonly GameClockEventDispatcher _clockDispatcher = new();
    private readonly WorldRules _rules;
    private string? _pendingPassOutStatId;

    public WorldState(
        Player player,
        IEnumerable<Npc> npcs,
        IEnumerable<Location> locations,
        IEnumerable<Item> items,
        IEnumerable<SkillDef> skills,
        IEnumerable<ActionDefinition> actions,
        GameClock? initialClock = null,
        WorldRules? rules = null)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));

        _npcs = Index(npcs, "NPC");
        _locations = Index(locations, "location");
        _items = Index(items, "item");
        _skills = Index(skills, "skill");
        _actions = Index(actions, "action");

        _rules = rules ?? WorldRules.Default;
        Clock = initialClock ?? new GameClock(0, 8, 0);
        Flags = [];
        Unlocks = [];
        Journal = new EventJournal();

        // Decay is driven by the clock seam (ADR-011): the dispatcher raises HourPassed once per
        // hour boundary crossed and this single subscription applies one decay tick per boundary.
        // Subscribed exactly once, at construction; the dispatcher is private so this is the only
        // owner of the advance and no new public engine API is introduced.
        _clockDispatcher.HourPassed += Player.Stats.ApplyHourPassed;

        // D6/ADR-012: a PassOut crossing only *announces* itself here and mutates nothing, so it
        // can never re-enter the dispatcher from inside the HourPassed callback above. The
        // consequence is drained by ActionResolver.Apply after its own advance completes.
        Player.Stats.StatCritical += OnPlayerStatCritical;
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

    /// <summary>
    /// Advances the clock forward through the private <see cref="GameClockEventDispatcher"/>, which
    /// is the single owner of the advance: it raises <c>HourPassed</c> once per hour boundary
    /// crossed (driving player decay) and reports the boundaries crossed and whether a day started.
    /// </summary>
    internal (int BoundariesCrossed, bool DayStarted) AdvanceClock(int minutes)
    {
        var (next, boundariesCrossed, dayStarted) = _clockDispatcher.Advance(Clock, minutes);
        Clock = next;
        return (boundariesCrossed, dayStarted);
    }

    /// <summary>
    /// Latches a pending pass-out announced by <see cref="StatSet.StatCritical"/> and mutates
    /// nothing: completing the consequence from inside a clock callback would re-enter the
    /// dispatcher (ADR-011/ADR-012).
    /// </summary>
    private void OnPlayerStatCritical(StatCriticalEventArgs args)
    {
        if (args.Consequence == StatConsequence.PassOut)
        {
            _pendingPassOutStatId = args.StatId;
        }
    }

    /// <summary>
    /// Completes a latched pass-out (ADR-012): advances the clock by <see cref="WorldRules.ForcedSleepHours"/>
    /// hours through the same dispatcher, so exactly one decay tick fires per boundary crossed and
    /// no manual <c>ApplyHourPassed</c> loop is used; applies the mood penalty exactly once; and
    /// journals one <see cref="JournalEntryTypes.PassOut"/> entry (plus a
    /// <see cref="JournalEntryTypes.DayStarted"/> entry when the sleep rolls the day).
    /// </summary>
    internal void DrainPendingPassOut()
    {
        var statId = _pendingPassOutStatId;
        if (statId is null)
        {
            return;
        }

        // Clear before the forced-sleep advance: a crossing raised during the sleep then latches a
        // *new* pending pass-out for the next drain instead of re-draining this one (no recursion).
        _pendingPassOutStatId = null;

        var passOutClock = Clock;
        var sleptHours = _rules.ForcedSleepHours;

        var (_, dayStarted) = AdvanceClock(sleptHours * GameClock.MinutesPerHour);

        if (_rules.PassOutMoodPenalty != 0m && Player.Stats.Get(_rules.PassOutMoodStatId) is not null)
        {
            Player.Stats.ApplyDelta(_rules.PassOutMoodStatId, _rules.PassOutMoodPenalty);
        }

        Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            passOutClock,
            JournalEntryTypes.PassOut,
            $"{statId}:{sleptHours.ToString(CultureInfo.InvariantCulture)}");

        if (dayStarted)
        {
            Journal.Append(
                TurnCorrelation.Current ?? string.Empty,
                Clock,
                JournalEntryTypes.DayStarted,
                Clock.DayIndex.ToString(CultureInfo.InvariantCulture));
        }
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
