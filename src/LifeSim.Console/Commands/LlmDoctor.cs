using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LifeSim.Console.Commands;

/// <summary>
/// Outcome of a <see cref="LlmDoctor"/> probe against a local LLM endpoint.
/// </summary>
/// <param name="EndpointReachable">True when the server answered an HTTP request, regardless of status.</param>
/// <param name="ModelPresent">True when the configured model was found in the <c>/models</c> listing.</param>
/// <param name="Endpoint">The configured base endpoint.</param>
/// <param name="ModelsUri">The URL actually probed for the model listing.</param>
/// <param name="Model">The configured model identifier.</param>
/// <param name="AvailableModels">Model identifiers reported by the endpoint, when enumerable.</param>
/// <param name="Latency">Wall-clock duration of the probe.</param>
/// <param name="StatusCode">HTTP status code when an HTTP response was received; null on transport failure.</param>
/// <param name="Error">Diagnostic detail when the probe did not fully succeed.</param>
public sealed record LlmDoctorResult(
    bool EndpointReachable,
    bool ModelPresent,
    string Endpoint,
    string ModelsUri,
    string Model,
    IReadOnlyList<string> AvailableModels,
    TimeSpan Latency,
    int? StatusCode,
    string? Error);

/// <summary>
/// Probes a local OpenAI-compatible LLM endpoint for reachability and model presence.
/// Reachability is established with a <c>GET {endpoint}/models</c> request; model presence
/// is an ordinal, case-insensitive match of the configured model id against the listing.
/// The HTTP transport is injected via <see cref="HttpMessageHandler"/> so tests never touch
/// the network.
/// </summary>
public sealed class LlmDoctor
{
    /// <summary>Default probe timeout, matching the startup health-check budget.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;

    public LlmDoctor(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _httpClient = new HttpClient(handler)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
    }

    public async Task<LlmDoctorResult> CheckAsync(
        string endpoint,
        string model,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var modelsUri = BuildModelsUri(endpoint);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new LlmDoctorResult(
                    EndpointReachable: true,
                    ModelPresent: false,
                    endpoint,
                    modelsUri,
                    model,
                    AvailableModels: [],
                    stopwatch.Elapsed,
                    statusCode,
                    Error: $"Endpoint answered HTTP {statusCode} ({response.ReasonPhrase}) instead of 2xx.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            IReadOnlyList<string> availableModels;
            try
            {
                availableModels = ParseModels(body);
            }
            catch (JsonException)
            {
                return new LlmDoctorResult(
                    EndpointReachable: true,
                    ModelPresent: false,
                    endpoint,
                    modelsUri,
                    model,
                    AvailableModels: [],
                    stopwatch.Elapsed,
                    statusCode,
                    Error: "Endpoint is reachable but returned an unparseable /models payload.");
            }

            var modelPresent = availableModels.Contains(model, StringComparer.OrdinalIgnoreCase);

            return new LlmDoctorResult(
                EndpointReachable: true,
                ModelPresent: modelPresent,
                endpoint,
                modelsUri,
                model,
                availableModels,
                stopwatch.Elapsed,
                statusCode,
                Error: modelPresent ? null : "Configured model was not found in the /models listing.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            return new LlmDoctorResult(
                EndpointReachable: false,
                ModelPresent: false,
                endpoint,
                modelsUri,
                model,
                AvailableModels: [],
                stopwatch.Elapsed,
                StatusCode: null,
                Error: ex is TaskCanceledException
                    ? $"No response within {_httpClient.Timeout.TotalSeconds:0}s (is the server running?)."
                    : ex.Message);
        }
    }

    private static string BuildModelsUri(string endpoint) =>
        $"{endpoint.TrimEnd('/')}/models";

    private static IReadOnlyList<string> ParseModels(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                models.Add(id.GetString()!);
            }
        }

        return models;
    }
}
