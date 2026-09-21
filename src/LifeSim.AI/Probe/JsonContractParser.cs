using System.Text.Json;

namespace LifeSim.AI.Probe;

/// <summary>
/// Strict JSON parsing for probe contracts. The only leniency is trimming markdown code
/// fences (<c>```json … ```</c>) that small local models frequently emit around JSON.
/// </summary>
public static class JsonContractParser
{
    /// <summary>
    /// Parses <paramref name="raw"/> as JSON after trimming fences and whitespace.
    /// Returns null (with an <paramref name="error"/>) on any failure — no exceptions leak.
    /// The returned <see cref="JsonDocument"/> must be disposed by the caller.
    /// </summary>
    public static JsonDocument? TryParseDocument(string raw, out string? error)
    {
        error = null;

        var trimmed = TrimFences(raw).Trim();
        if (trimmed.Length == 0)
        {
            error = "empty response";
            return null;
        }

        try
        {
            return JsonDocument.Parse(trimmed);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Removes a single leading and trailing markdown code fence, when present.
    /// </summary>
    public static string TrimFences(string raw)
    {
        var text = raw.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = text.IndexOf('\n');
            text = newline >= 0 ? text[(newline + 1)..] : text[3..];
            text = text.TrimStart();
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3].TrimEnd();
        }

        return text;
    }
}
