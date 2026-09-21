using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LifeSim.AI.Probe;

/// <summary>
/// Raw OpenAI-compatible chat-completions sender used by the probe harness. Honors the
/// configured endpoint, model and timeout; sends <c>temperature=0</c>, <c>stream=false</c>
/// for deterministic, single-shot responses.
/// </summary>
public sealed class ProbeSender : IChatCompletionsSender
{
    /// <summary>Default per-call timeout. Local CPU inference can be slow, so this is generous.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public ProbeSender(
        HttpMessageHandler handler,
        string endpoint,
        string model,
        string? apiKey = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _endpoint = endpoint;
        _model = model;
        _apiKey = apiKey;
        _httpClient = new HttpClient(handler) { Timeout = timeout ?? DefaultTimeout };
    }

    public async Task<string> SendAsync(
        IReadOnlyList<ProbeMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var uri = $"{_endpoint.TrimEnd('/')}/chat/completions";

        var payload = new
        {
            model = _model,
            temperature = 0.0,
            stream = false,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Chat completions returned HTTP {(int)response.StatusCode}: {Truncate(body)}");
        }

        return ParseCompletionContent(body);
    }

    private static string ParseCompletionContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        if (!choices[0].TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? string.Empty
            : content.GetRawText();
    }

    private static string Truncate(string text, int max = 300) =>
        text.Length <= max ? text : text[..max] + "…";
}
