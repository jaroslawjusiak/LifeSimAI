namespace LifeSim.Core.Actions;

/// <summary>
/// A precondition an action must satisfy before it can resolve. Every kind is evaluated
/// read-only against the world; a failed requirement produces a human-readable reason and
/// leaves state untouched.
/// </summary>
public abstract record ActionRequirement
{
    /// <summary>A stat must be at least a given value.</summary>
    public sealed record StatGte(string StatId, decimal Minimum) : ActionRequirement;

    /// <summary>A skill must be at least a given level.</summary>
    public sealed record SkillGte(string SkillId, int MinimumLevel) : ActionRequirement;

    /// <summary>The player must be at a specific location.</summary>
    public sealed record LocationIs(string LocationId) : ActionRequirement;

    /// <summary>The player must be at a location of a specific type.</summary>
    public sealed record LocationType(string Type) : ActionRequirement;

    /// <summary>Relationship with an NPC must be at least a given value.</summary>
    public sealed record RelationshipGte(string NpcId, int Minimum) : ActionRequirement;

    /// <summary>The player must hold at least a quantity of an item.</summary>
    public sealed record HasItem(string ItemId, int Quantity = 1) : ActionRequirement;

    /// <summary>A world flag must be set.</summary>
    public sealed record FlagSet(string Flag) : ActionRequirement;

    /// <summary>The current hour must fall inside a window (overnight ranges wrap).</summary>
    public sealed record TimeWindow(int StartHour, int EndHour) : ActionRequirement;
}
