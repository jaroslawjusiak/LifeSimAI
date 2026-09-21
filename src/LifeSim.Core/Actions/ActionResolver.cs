using System.Globalization;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;

namespace LifeSim.Core.Actions;

/// <summary>
/// Resolves an action against the world. All preconditions and effect references are
/// validated read-only first; only then are effects, fixed costs and the clock advance
/// applied — so a failed resolution provably mutates nothing and a successful one is atomic.
/// </summary>
public static class ActionResolver
{
    /// <summary>
    /// Resolves <paramref name="actionId"/> against <paramref name="world"/>, optionally with
    /// a target (a destination location for <c>Move</c>). Returns success or ordered reasons.
    /// </summary>
    public static ActionResult Resolve(WorldState world, string actionId, string? targetId = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var action = world.GetAction(actionId);
        if (action is null)
        {
            return ActionResult.Failure(actionId, [$"Unknown action id '{actionId}'."]);
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
            return ActionResult.Failure(actionId, failures);
        }

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

        var (hoursPassed, dayStarted) = world.AdvanceClock(action.TimeCostMinutes);
        for (var i = 0; i < hoursPassed; i++)
        {
            world.Player.Stats.ApplyHourPassed();
        }

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

        return ActionResult.Success(actionId);
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
