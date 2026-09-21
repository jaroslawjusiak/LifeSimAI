using System.Globalization;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;

namespace LifeSim.Core.Actions;

/// <summary>
/// The outcome of the read-only validation phase: either the resolved action (ready to apply)
/// or the ordered, human-readable reasons it cannot run. Engine-internal: the public entry
/// point stays <see cref="ActionResolver.Resolve"/>.
/// </summary>
internal sealed record ActionValidation(bool IsSuccess, ActionDefinition? Action, IReadOnlyList<string> Reasons)
{
    public static ActionValidation Valid(ActionDefinition action) => new(true, action, []);

    public static ActionValidation Invalid(IReadOnlyList<string> reasons) => new(false, null, reasons);
}

/// <summary>
/// Resolves an action against the world in two phases. <see cref="Validate"/> is read-only:
/// all preconditions and effect references are evaluated and ordered reasons are returned on
/// failure, with no domain-state mutation (it records an <see cref="JournalEntryTypes.ActionFailed"/>
/// history entry so failures are observable). <see cref="Apply"/> then mutates effects, fixed
/// costs and the clock for a validated action. <see cref="Resolve"/> is the original
/// validate-then-apply facade; a failed resolution provably mutates nothing.
/// </summary>
public static class ActionResolver
{
    /// <summary>
    /// Resolves <paramref name="actionId"/> against <paramref name="world"/>, optionally with
    /// a target (a destination location for <c>Move</c>). Returns success or ordered reasons.
    /// </summary>
    public static ActionResult Resolve(WorldState world, string actionId, string? targetId = null)
    {
        var validation = Validate(world, actionId, targetId);
        return validation.IsSuccess
            ? Apply(world, validation, targetId)
            : ActionResult.Failure(actionId, validation.Reasons);
    }

    /// <summary>
    /// Read-only validation phase. Returns the resolved action when every requirement and effect
    /// reference is satisfied; otherwise ordered reasons and no world-state mutation. A failed
    /// validation appends an <see cref="JournalEntryTypes.ActionFailed"/> entry.
    /// </summary>
    internal static ActionValidation Validate(WorldState world, string actionId, string? targetId = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var action = world.GetAction(actionId);
        if (action is null)
        {
            var unknown = new[] { $"Unknown action id '{actionId}'." };
            JournalFailure(world, actionId, unknown);
            return ActionValidation.Invalid(unknown);
        }

        var failures = new List<string>();

        foreach (var requirement in action.Requirements)
        {
            if (!EvaluateRequirement(world, requirement, out var reason))
            {
                failures.Add(reason);
            }
        }

        foreach (var effect in action.Effects)
        {
            if (!ValidateEffectReference(world, effect, targetId, out var reason))
            {
                failures.Add(reason);
            }
        }

        if (action.EnergyCost != 0m && world.Player.Stats.Get("energy") is null)
        {
            failures.Add($"Action '{action.Id}' charges energy but the player has no 'energy' stat.");
        }

        if (failures.Count > 0)
        {
            JournalFailure(world, actionId, failures);
            return ActionValidation.Invalid(failures);
        }

        return ActionValidation.Valid(action);
    }

    /// <summary>
    /// Mutating application phase. Applies effects, fixed costs and the clock advance, then
    /// journals success. Requires a successful <see cref="Validate"/> result.
    /// </summary>
    internal static ActionResult Apply(WorldState world, ActionValidation validation, string? targetId = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(validation);
        if (!validation.IsSuccess || validation.Action is null)
        {
            throw new ArgumentException("Only a successful validation can be applied.", nameof(validation));
        }

        var action = validation.Action;

        foreach (var effect in action.Effects)
        {
            ApplyEffect(world, effect, targetId);
        }

        if (action.EnergyCost != 0m)
        {
            world.Player.Stats.ApplyDelta("energy", -action.EnergyCost);
        }

        if (action.MoneyCost != 0m)
        {
            world.Player.Money -= action.MoneyCost;
        }

        // Decay is no longer applied here: WorldState owns the clock seam and its dispatcher
        // raises HourPassed once per hour boundary crossed, which decays the player (ADR-011).
        var (_, dayStarted) = world.AdvanceClock(action.TimeCostMinutes);

        world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            world.Clock,
            JournalEntryTypes.ActionResolved,
            action.Id);

        if (dayStarted)
        {
            world.Journal.Append(
                TurnCorrelation.Current ?? string.Empty,
                world.Clock,
                JournalEntryTypes.DayStarted,
                world.Clock.DayIndex.ToString(CultureInfo.InvariantCulture));
        }

        // D6/ADR-012: a pass-out latched during the action (its effects, costs or the advance)
        // is completed only now — after the action's own advance and journaling — so the forced
        // sleep never re-enters the dispatcher from inside an HourPassed handler.
        world.DrainPendingPassOut();

        return ActionResult.Success(action.Id);
    }

    private static void JournalFailure(WorldState world, string actionId, IReadOnlyList<string> reasons)
    {
        world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            world.Clock,
            JournalEntryTypes.ActionFailed,
            $"{actionId}: {string.Join("; ", reasons)}");
    }

    private static bool EvaluateRequirement(WorldState world, ActionRequirement requirement, out string reason)
    {
        reason = string.Empty;

        switch (requirement)
        {
            case ActionRequirement.StatGte s:
            {
                var stat = world.Player.Stats.Get(s.StatId);
                if (stat is null)
                {
                    reason = $"Unknown stat '{s.StatId}'.";
                    return false;
                }

                if (stat.Value < s.Minimum)
                {
                    reason = $"Requires {s.StatId} ≥ {s.Minimum} (current {stat.Value}).";
                    return false;
                }

                return true;
            }

            case ActionRequirement.SkillGte sk:
            {
                if (world.GetSkill(sk.SkillId) is null)
                {
                    reason = $"Unknown skill '{sk.SkillId}'.";
                    return false;
                }

                var level = world.Player.Skills.LevelOf(sk.SkillId);
                if (level < sk.MinimumLevel)
                {
                    reason = $"Requires {sk.SkillId} level {sk.MinimumLevel} (current {level}).";
                    return false;
                }

                return true;
            }

            case ActionRequirement.LocationIs loc:
                if (world.Player.LocationId != loc.LocationId)
                {
                    reason = $"Requires being at '{loc.LocationId}'.";
                    return false;
                }

                return true;

            case ActionRequirement.LocationType lt:
            {
                var current = world.GetLocation(world.Player.LocationId);
                if (current?.Type != lt.Type)
                {
                    reason = $"Requires a '{lt.Type}' location.";
                    return false;
                }

                return true;
            }

            case ActionRequirement.RelationshipGte rel:
            {
                var npc = world.GetNpc(rel.NpcId);
                if (npc is null)
                {
                    reason = $"Unknown NPC '{rel.NpcId}'.";
                    return false;
                }

                if (npc.Relationship.Value < rel.Minimum)
                {
                    reason = $"Requires relationship ≥ {rel.Minimum} with '{rel.NpcId}' (current {npc.Relationship.Value}).";
                    return false;
                }

                return true;
            }

            case ActionRequirement.HasItem item:
            {
                var held = world.Player.Inventory.Where(i => i.ItemId == item.ItemId).Sum(i => i.Quantity);
                if (held < item.Quantity)
                {
                    reason = $"Requires {item.Quantity}× '{item.ItemId}'.";
                    return false;
                }

                return true;
            }

            case ActionRequirement.FlagSet flag:
                if (!world.Flags.Contains(flag.Flag))
                {
                    reason = $"Requires flag '{flag.Flag}'.";
                    return false;
                }

                return true;

            case ActionRequirement.TimeWindow tw:
            {
                var hour = world.Clock.Hour;
                var open = tw.StartHour < tw.EndHour
                    ? hour >= tw.StartHour && hour < tw.EndHour
                    : hour >= tw.StartHour || hour < tw.EndHour;

                if (!open)
                {
                    reason = $"Only available {tw.StartHour:00}:00–{tw.EndHour:00}:00.";
                    return false;
                }

                return true;
            }

            default:
                reason = $"Unsupported requirement '{requirement.GetType().Name}'.";
                return false;
        }
    }

    private static bool ValidateEffectReference(WorldState world, ActionEffect effect, string? targetId, out string reason)
    {
        reason = string.Empty;

        switch (effect)
        {
            case ActionEffect.StatDelta sd:
                if (world.Player.Stats.Get(sd.StatId) is null)
                {
                    reason = $"Effect references unknown stat '{sd.StatId}'.";
                    return false;
                }

                return true;

            case ActionEffect.SkillXp sx:
                if (world.GetSkill(sx.SkillId) is null)
                {
                    reason = $"Effect references unknown skill '{sx.SkillId}'.";
                    return false;
                }

                return true;

            case ActionEffect.Relationship rel:
                if (world.GetNpc(rel.NpcId) is null)
                {
                    reason = $"Effect references unknown NPC '{rel.NpcId}'.";
                    return false;
                }

                return true;

            case ActionEffect.Move move:
                if (world.GetLocation(move.LocationId ?? targetId ?? string.Empty) is null)
                {
                    reason = $"Unknown target location '{move.LocationId ?? targetId}'.";
                    return false;
                }

                return true;

            case ActionEffect.Money:
            case ActionEffect.SetFlag:
            case ActionEffect.Unlock:
                return true;

            default:
                reason = $"Unsupported effect '{effect.GetType().Name}'.";
                return false;
        }
    }

    private static void ApplyEffect(WorldState world, ActionEffect effect, string? targetId)
    {
        switch (effect)
        {
            case ActionEffect.StatDelta sd:
                world.Player.Stats.ApplyDelta(sd.StatId, sd.Amount);
                break;

            case ActionEffect.Money m:
                world.Player.Money += m.Amount;
                break;

            case ActionEffect.SkillXp sx:
                world.Player.Skills.AddXp(sx.SkillId, sx.Amount);
                break;

            case ActionEffect.Relationship rel:
                world.GetNpc(rel.NpcId)!.Relationship.ApplyDelta(rel.Amount);
                break;

            case ActionEffect.SetFlag f:
                world.Flags.Add(f.Flag);
                break;

            case ActionEffect.Move move:
                world.Player.LocationId = move.LocationId ?? targetId!;
                break;

            case ActionEffect.Unlock u:
                world.Unlocks.Add(u.Id);
                break;

            default:
                throw new InvalidOperationException($"Unsupported effect '{effect.GetType().Name}'.");
        }
    }
}
