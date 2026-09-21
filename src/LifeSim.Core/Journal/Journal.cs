using LifeSim.Core.Time;

namespace LifeSim.Core.Journal;

/// <summary>
/// Well-known journal entry types.
/// </summary>
public static class JournalEntryTypes
{
    /// <summary>Always-in-context facts (world id, player name).</summary>
    public const string PinnedFact = "PinnedFact";

    /// <summary>A turn began.</summary>
    public const string TurnStarted = "TurnStarted";

    /// <summary>A stage of the turn pipeline transitioned.</summary>
    public const string StageTransition = "StageTransition";

    /// <summary>The raw player input for a turn.</summary>
    public const string RawInput = "RawInput";

    /// <summary>The translated canonical command for a turn.</summary>
    public const string TranslatedCommand = "TranslatedCommand";

    /// <summary>An action was resolved successfully. Payload is the bare action id.</summary>
    public const string ActionResolved = "ActionResolved";

    /// <summary>An action failed validation. Payload is the action id and its ordered reasons.</summary>
    public const string ActionFailed = "ActionFailed";

    /// <summary>A pipeline stage failed. Payload is the stage and the error message.</summary>
    public const string StageFailed = "StageFailed";

    /// <summary>A new day started.</summary>
    public const string DayStarted = "DayStarted";

    /// <summary>The player passed out and was forced to sleep. Payload is "&lt;statId&gt;:&lt;sleptHours&gt;".</summary>
    public const string PassOut = "PassOut";
}

/// <summary>
/// A single append-only journal record.
/// </summary>
/// <param name="Seq">Monotonic sequence number (1-based).</param>
/// <param name="CorrelationId">Turn correlation id that produced this entry.</param>
/// <param name="SimTime">Simulation clock at the time of the entry.</param>
/// <param name="Type">Entry type (see <see cref="JournalEntryTypes"/>).</param>
/// <param name="Payload">Optional detail.</param>
/// <param name="IsPinned">True for always-in-context facts; false for evictable rolling entries.</param>
public sealed record JournalEntry(
    long Seq,
    string CorrelationId,
    GameClock SimTime,
    string Type,
    string? Payload,
    bool IsPinned);

/// <summary>
/// The append-only event journal: the single source of truth shared by saves, AI context
/// building, debugging and replay. Entries are appended in sequence and never mutated.
/// </summary>
public sealed class EventJournal
{
    private readonly List<JournalEntry> _entries = [];
    private long _nextSeq = 1;

    /// <summary>All entries in append order.</summary>
    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>Appends a new entry and returns it.</summary>
    public JournalEntry Append(
        string correlationId,
        GameClock simTime,
        string type,
        string? payload = null,
        bool isPinned = false)
    {
        var entry = new JournalEntry(_nextSeq++, correlationId, simTime, type, payload, isPinned);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>The most recent <paramref name="count"/> entries, in ascending sequence order.</summary>
    public IReadOnlyList<JournalEntry> Last(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var skip = Math.Max(0, _entries.Count - count);
        return _entries.Skip(skip).ToList();
    }

    /// <summary>All entries of a given type, in append order.</summary>
    public IReadOnlyList<JournalEntry> ByType(string type) =>
        _entries.Where(e => e.Type == type).ToList();

    /// <summary>All entries at or after a day index, in append order.</summary>
    public IReadOnlyList<JournalEntry> SinceDay(int dayIndex) =>
        _entries.Where(e => e.SimTime.DayIndex >= dayIndex).ToList();

    /// <summary>Always-in-context (pinned) entries.</summary>
    public IReadOnlyList<JournalEntry> Pinned() => _entries.Where(e => e.IsPinned).ToList();

    /// <summary>Evictable (rolling) entries.</summary>
    public IReadOnlyList<JournalEntry> Rolling() => _entries.Where(e => !e.IsPinned).ToList();
}
