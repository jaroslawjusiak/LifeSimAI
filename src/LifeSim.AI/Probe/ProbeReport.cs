using System.Text;

namespace LifeSim.AI.Probe;

/// <summary>
/// Formats probe results as the model acceptance record (markdown) and computes the overall
/// pass/fail gate. The gate ignores the informational roleplay case.
/// </summary>
public static class ProbeReport
{
    /// <summary>True when every JSON-contract case passed. Roleplay is informational and does not gate.</summary>
    public static bool OverallPass(IReadOnlyList<ProbeResult> results) =>
        results.Where(r => r.Kind != ProbeCaseKind.Roleplay).All(r => r.Success);

    /// <summary>Renders the acceptance record as markdown.</summary>
    public static string Format(
        string model,
        string endpoint,
        IReadOnlyList<ProbeResult> results,
        DateTimeOffset? timestampUtc = null)
    {
        var stamp = timestampUtc ?? DateTimeOffset.UtcNow;
        var overall = OverallPass(results) ? "PASS" : "FAIL";

        var builder = new StringBuilder();
        builder.AppendLine("# Model Acceptance Record");
        builder.AppendLine();
        builder.AppendLine($"- **Model:** {model}");
        builder.AppendLine($"- **Endpoint:** {endpoint}");
        builder.AppendLine($"- **Timestamp (UTC):** {stamp:O}");
        builder.AppendLine();
        builder.AppendLine("| Case | Contract | Result | Latency (ms) | Note |");
        builder.AppendLine("|------|----------|--------|--------------|------|");

        foreach (var result in results)
        {
            builder.AppendLine(
                $"| {result.Name} | {result.Kind} | {(result.Success ? "pass" : "FAIL")} | {result.LatencyMs} | {EscapeCell(result.Error ?? result.Detail ?? string.Empty)} |");
        }

        builder.AppendLine();
        builder.AppendLine($"**Overall:** {overall}");
        return builder.ToString();
    }

    private static string EscapeCell(string text) =>
        text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
