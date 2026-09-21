namespace LifeSim.Core.Actions;

/// <summary>
/// A state mutation an action applies on success. Effects are applied atomically after all
/// requirements and references have been validated, so a mid-apply failure cannot occur.
/// </summary>
public abstract record ActionEffect
{
    /// <summary>Adds a (clamped) delta to a stat.</summary>
    public sealed record StatDelta(string StatId, decimal Amount) : ActionEffect;

    /// <summary>Adjusts the player's money.</summary>
    public sealed record Money(decimal Amount) : ActionEffect;

    /// <summary>Grants XP to a skill (training it if unknown).</summary>
    public sealed record SkillXp(string SkillId, int Amount) : ActionEffect;

    /// <summary>Adjusts the relationship with an NPC (clamped to ±100).</summary>
    public sealed record Relationship(string NpcId, int Amount) : ActionEffect;

    /// <summary>Sets a world flag.</summary>
    public sealed record SetFlag(string Flag) : ActionEffect;

    /// <summary>Moves the player to a location: the baked <see cref="LocationId"/> when set, else the resolved target.</summary>
    public sealed record Move(string? LocationId = null) : ActionEffect;

    /// <summary>Unlocks a content id (skill/location/action).</summary>
    public sealed record Unlock(string Id) : ActionEffect;
}
