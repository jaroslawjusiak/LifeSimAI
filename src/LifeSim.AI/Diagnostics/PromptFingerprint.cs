using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace LifeSim.AI.Diagnostics;

/// <summary>
/// Produces a stable SHA-256 fingerprint for an LLM request so identical inputs are
/// recognizable without storing (or comparing) raw prompts.
/// </summary>
public static class PromptFingerprint
{
    public static string Compute(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            builder.Append(message.Role.Value).Append('\u001f').Append(message.Text).Append('\u001e');
        }

        builder.Append('\u001f')
            .Append(options?.ModelId ?? string.Empty).Append('\u001f')
            .Append(options?.Temperature?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty).Append('\u001f')
            .Append(options?.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
