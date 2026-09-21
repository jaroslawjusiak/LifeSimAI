namespace LifeSim.Core.Entities;

/// <summary>
/// A directed edge out of a location. One-way by default — the target location does not
/// automatically gain a connection back. An optional <see cref="RequiresFlag"/> gates the
/// edge behind a player/NPC flag (e.g. a key).
/// </summary>
public sealed record LocationConnection(string TargetId, string? RequiresFlag = null);

/// <summary>A half-open opening-hours window, <c>[StartHour, EndHour)</c>.</summary>
public sealed record TimeWindow(int StartHour, int EndHour)
{
    public bool Contains(int hour) => hour >= StartHour && hour < EndHour;
}

/// <summary>
/// A place in the world: its connections to other locations (possibly one-way and/or gated),
/// the actions allowed there, and optional opening hours.
/// </summary>
public sealed class Location : IEntityWithId
{
    public Location(
        string id,
        string name,
        IEnumerable<LocationConnection> connections,
        IEnumerable<string>? allowedActionIds = null,
        TimeWindow? openHours = null,
        string? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        Connections = connections.ToList();
        AllowedActionIds = allowedActionIds?.ToList() ?? [];
        OpenHours = openHours;
        Type = type;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Directed connections to other locations.</summary>
    public IReadOnlyList<LocationConnection> Connections { get; }

    /// <summary>Ids of actions that can be performed at this location.</summary>
    public IReadOnlyList<string> AllowedActionIds { get; }

    /// <summary>Opening hours, or null when the location is always open.</summary>
    public TimeWindow? OpenHours { get; }

    /// <summary>Optional location type (e.g. "home", "shop", "workplace"), used by LocationType requirements.</summary>
    public string? Type { get; }

    /// <summary>Whether the location is open at the given hour (always true without opening hours).</summary>
    public bool IsOpen(int hour) => OpenHours is null || OpenHours.Contains(hour);
}
