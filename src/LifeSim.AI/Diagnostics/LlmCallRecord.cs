using System.Text.Json.Serialization;

namespace LifeSim.AI.Diagnostics;

/// <summary>
/// A single LLM interaction, serialized as one line in <c>llm-calls.jsonl</c>.
/// This is the durable record used for debugging and, later, prompt-regression fixtures.
/// </summary>
public sealed record LlmCallRecord
{
    /// <summary>Turn correlation id (see <see cref="LifeSim.Core.Diagnostics.TurnCorrelation"/>).</summary>
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; init; }

    /// <summary>Logical agent that issued the call (Narrator, Translator, Options, Npc, Director).</summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    /// <summary>Model identifier reported by the endpoint, when known.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>Stable SHA-256 fingerprint of the request + model + call parameters.</summary>
    [JsonPropertyName("promptHash")]
    public required string PromptHash { get; init; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    [JsonPropertyName("latencyMs")]
    public long LatencyMs { get; init; }

    /// <summary>Model finish reason (stop, length, tool_calls, content_filter), when reported.</summary>
    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; init; }

    /// <summary>Whether the call completed without throwing.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Failure detail when <see cref="Success"/> is false.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Request messages. Contents are redacted when redaction is enabled.</summary>
    [JsonPropertyName("request")]
    public IReadOnlyList<LlmMessageRecord> Request { get; init; } = [];

    /// <summary>Aggregated response text. Redacted when redaction is enabled.</summary>
    [JsonPropertyName("response")]
    public string? Response { get; init; }

    /// <summary>UTC timestamp of when the record was produced.</summary>
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A request message captured in an <see cref="LlmCallRecord"/>.</summary>
public sealed record LlmMessageRecord(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);
