using LifeSim.Core.Entities;

namespace LifeSim.Core.Actions;

/// <summary>
/// The declarative definition of an action: its verb, menu category, fixed costs (time,
/// energy, money), preconditions and effects.
/// </summary>
public sealed record ActionDefinition(
    string Id,
    ActionVerb Verb,
    string Category,
    int TimeCostMinutes,
    decimal EnergyCost,
    decimal MoneyCost,
    IReadOnlyList<ActionRequirement> Requirements,
    IReadOnlyList<ActionEffect> Effects) : IEntityWithId;
