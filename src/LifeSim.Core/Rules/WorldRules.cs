namespace LifeSim.Core.Rules;

/// <summary>
/// World-level rules that parameterise engine consequences the content layer configures
/// (M2-06 populates these from the world's <c>rules/</c> folder). Kept BCL-only and free of
/// application configuration so the domain stays deterministic (ADR-009, ADR-012).
/// </summary>
/// <remarks>
/// The pass-out consequence is selected per stat on <see cref="Stats.StatDef"/>; its
/// <em>parameters</em> live here, mirroring how starvation keeps its drain parameters off
/// <see cref="Stats.StatDef"/> (ADR-012).
/// </remarks>
public sealed record WorldRules
{
    private const int DefaultSleepHours = 6;
    private const decimal DefaultMoodPenalty = -10m;
    private const string DefaultMoodStatId = "mood";

    /// <summary>The shipped defaults: a 6-hour forced sleep and a −10 mood penalty on "mood".</summary>
    public static WorldRules Default { get; } = new();

    /// <summary>
    /// Creates world rules. <paramref name="forcedSleepHours"/> must be non-negative and
    /// <paramref name="passOutMoodPenalty"/> non-positive; <paramref name="passOutMoodStatId"/>
    /// must be non-blank.
    /// </summary>
    public WorldRules(
        int forcedSleepHours = DefaultSleepHours,
        decimal passOutMoodPenalty = DefaultMoodPenalty,
        string passOutMoodStatId = DefaultMoodStatId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(forcedSleepHours);
        if (passOutMoodPenalty > 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(passOutMoodPenalty),
                passOutMoodPenalty,
                "The pass-out mood penalty must be non-positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(passOutMoodStatId);

        ForcedSleepHours = forcedSleepHours;
        PassOutMoodPenalty = passOutMoodPenalty;
        PassOutMoodStatId = passOutMoodStatId;
    }

    /// <summary>Hours of forced sleep applied when the player passes out (non-negative).</summary>
    public int ForcedSleepHours { get; }

    /// <summary>Mood delta applied exactly once when the player passes out (non-positive).</summary>
    public decimal PassOutMoodPenalty { get; }

    /// <summary>Id of the stat the pass-out mood penalty is applied to.</summary>
    public string PassOutMoodStatId { get; }
}
