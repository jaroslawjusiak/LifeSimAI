using LifeSim.Core.Actions;

namespace LifeSim.Core.Turns;

/// <summary>
/// The outcome of running one turn: the final state, whether the action applied, the action
/// result, narration, options, and any recovered stage errors.
/// </summary>
public sealed record TurnResult(
    TurnState FinalState,
    bool IsSuccess,
    string? ActionId,
    ActionResult? ActionResult,
    string Narration,
    IReadOnlyList<string> Options,
    IReadOnlyList<TurnError> Errors);
