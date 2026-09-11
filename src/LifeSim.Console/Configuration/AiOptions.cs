using System.ComponentModel.DataAnnotations;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Master AI feature flag and global AI guardrail settings.
/// Bound from Ai:* configuration keys.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Master switch — when false every agent stage falls back to deterministic logic.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Cache LLM responses on disk for development replay (keyed by prompt hash).</summary>
    public bool EnableCache { get; init; } = false;
}
