namespace LifeSim.AI.Probe;

/// <summary>
/// The outcome of running one probe case.
/// </summary>
/// <param name="CaseId">Stable case id (matches <see cref="ProbeCase.Id"/>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Contract kind exercised.</param>
/// <param name="Success">Whether the case passed its contract.</param>
/// <param name="LatencyMs">Wall-clock duration of the case (both turns for the repair case).</param>
/// <param name="Error">Failure reason (parse error, exception, or null on success).</param>
/// <param name="Detail">Truncated model output, for the acceptance record.</param>
public sealed record ProbeResult(
    string CaseId,
    string Name,
    ProbeCaseKind Kind,
    bool Success,
    long LatencyMs,
    string? Error,
    string? Detail);
