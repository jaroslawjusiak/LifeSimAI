using System.ComponentModel.DataAnnotations;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Options for the local LLM endpoint and per-agent model settings.
/// Bound from Llm:* configuration keys.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>OpenAI-compatible base URL of the local LLM server (e.g. Jan).</summary>
    [Required]
    [Url]
    public string Endpoint { get; init; } = "http://127.0.0.1:1337/v1";

    /// <summary>Model identifier as reported by /v1/models.</summary>
    [Required]
    [MinLength(1)]
    public string Model { get; init; } = "default";

    /// <summary>
    /// Optional bearer token for the local server. Empty when the server requires no auth
    /// (the historical Jan default). Sent as <c>Authorization: Bearer &lt;ApiKey&gt;</c>.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Per-agent overrides keyed by agent name (Narrator, Translator, Options, Npc, Director).</summary>
    public Dictionary<string, AgentModelOptions> Agents { get; init; } = [];
}

/// <summary>
/// Per-agent LLM call tuning parameters.
/// </summary>
public sealed class AgentModelOptions
{
    [Range(0.0, 2.0)]
    public double Temperature { get; init; } = 0.7;

    [Range(64, 8192)]
    public int MaxTokens { get; init; } = 1024;

    [Range(5, 300)]
    public int TimeoutSeconds { get; init; } = 45;
}
