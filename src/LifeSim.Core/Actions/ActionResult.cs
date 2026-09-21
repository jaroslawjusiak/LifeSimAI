namespace LifeSim.Core.Actions;

/// <summary>
/// The outcome of resolving an action: success, or failure with ordered, human-readable
/// reasons. On failure the world is guaranteed to be unchanged.
/// </summary>
public sealed record ActionResult(string ActionId, bool IsSuccess, IReadOnlyList<string> Reasons)
{
    public static ActionResult Success(string actionId) => new(actionId, true, []);

    public static ActionResult Failure(string actionId, IEnumerable<string> reasons) =>
        new(actionId, false, reasons.ToList());
}
