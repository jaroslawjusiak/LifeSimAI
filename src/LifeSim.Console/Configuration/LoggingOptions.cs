using System.ComponentModel.DataAnnotations;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Controls application log files and the LLM call journal.
/// Bound from Logging:* configuration keys.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    /// <summary>
    /// Directory for application logs and <c>llm-calls.jsonl</c>.
    /// Empty resolves to ~/.lifesim/logs.
    /// </summary>
    [Required]
    public string Directory { get; init; } = string.Empty;

    /// <summary>Maximum size of a single log file before rotation (bytes).</summary>
    [Range(1024, 1_073_741_824)]
    public long FileSizeLimitBytes { get; init; } = 1_000_000;

    /// <summary>Number of rotated files to retain.</summary>
    [Range(0, 100)]
    public int RetainedFileCount { get; init; } = 5;

    /// <summary>Total on-disk budget for the LLM call journal (bytes).</summary>
    [Range(1024, 10_737_418_240)]
    public long MaxDirectoryBytes { get; init; } = 10_000_000;

    /// <summary>
    /// When true, request and response contents are replaced with a placeholder in
    /// <c>llm-calls.jsonl</c>. Default false — this is a local, single-player app.
    /// </summary>
    public bool RedactSensitiveContent { get; init; }
}
