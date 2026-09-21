namespace LifeSim.AI.Probe;

/// <summary>
/// An authored probe case: an id, a display name, the contract kind it exercises, and the
/// initial chat messages. The repair case reuses <see cref="Messages"/> as its first turn.
/// </summary>
public sealed record ProbeCase(
    string Id,
    string Name,
    ProbeCaseKind Kind,
    IReadOnlyList<ProbeMessage> Messages);
