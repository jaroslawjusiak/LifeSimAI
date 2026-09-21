using System.Diagnostics;
using System.Text.Json;

namespace LifeSim.AI.Probe;

/// <summary>
/// Runs the authored probe cases against an <see cref="IChatCompletionsSender"/> and
/// classifies each outcome. Any failure of a JSON-contract case is a hard gate failure.
/// The repair case corrupts the model's first output and feeds the parser error back to prove
/// the model can fix its own invalid JSON.
/// </summary>
public sealed class ProbeRunner
{
    private readonly IChatCompletionsSender _sender;

    public ProbeRunner(IChatCompletionsSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ProbeResult>(ProbeCases.All.Count);
        foreach (var probeCase in ProbeCases.All)
        {
            results.Add(await RunCaseAsync(probeCase, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<ProbeResult> RunCaseAsync(ProbeCase probeCase, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return probeCase.Kind switch
            {
                ProbeCaseKind.Json => ValidateJson(
                    probeCase,
                    await _sender.SendAsync(probeCase.Messages, cancellationToken).ConfigureAwait(false),
                    stopwatch),
                ProbeCaseKind.RepairJson => await RunRepairAsync(probeCase, stopwatch, cancellationToken).ConfigureAwait(false),
                ProbeCaseKind.Roleplay => ValidateRoleplay(
                    probeCase,
                    await _sender.SendAsync(probeCase.Messages, cancellationToken).ConfigureAwait(false),
                    stopwatch),
                ProbeCaseKind.InjectionAsData => ValidateInjection(
                    probeCase,
                    await _sender.SendAsync(probeCase.Messages, cancellationToken).ConfigureAwait(false),
                    stopwatch),
                _ => throw new ArgumentOutOfRangeException(nameof(probeCase)),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProbeResult(probeCase.Id, probeCase.Name, probeCase.Kind, false, stopwatch.ElapsedMilliseconds, ex.Message, null);
        }
    }

    private async Task<ProbeResult> RunRepairAsync(
        ProbeCase probeCase,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var first = await _sender.SendAsync(probeCase.Messages, cancellationToken).ConfigureAwait(false);

        // Deliberately break the model's output, then feed the parser error back and ask it to fix it.
        var corrupted = Corrupt(first);
        JsonContractParser.TryParseDocument(corrupted, out var error)?.Dispose();

        var repairMessages = new List<ProbeMessage>(probeCase.Messages.Count + 2);
        repairMessages.AddRange(probeCase.Messages);
        repairMessages.Add(new ProbeMessage("assistant", first));
        repairMessages.Add(
            new ProbeMessage(
                "user",
                $"Your previous output was invalid JSON. Error: {error}. Previous output was:\n{corrupted}\nReturn only the corrected valid JSON and nothing else."));

        var repaired = await _sender.SendAsync(repairMessages, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        using var repairedDocument = JsonContractParser.TryParseDocument(repaired, out var parseError);
        var ok = repairedDocument is not null;

        return new ProbeResult(
            probeCase.Id,
            probeCase.Name,
            probeCase.Kind,
            ok,
            stopwatch.ElapsedMilliseconds,
            ok ? null : $"repair failed: {parseError}",
            ok ? null : Truncate(repaired));
    }

    private static ProbeResult ValidateJson(ProbeCase probeCase, string response, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        using var document = JsonContractParser.TryParseDocument(response, out var error);
        var ok = document is not null;
        return new ProbeResult(probeCase.Id, probeCase.Name, probeCase.Kind, ok, stopwatch.ElapsedMilliseconds, ok ? null : error, ok ? null : Truncate(response));
    }

    private static ProbeResult ValidateRoleplay(ProbeCase probeCase, string response, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var ok = !string.IsNullOrWhiteSpace(response);
        return new ProbeResult(probeCase.Id, probeCase.Name, probeCase.Kind, ok, stopwatch.ElapsedMilliseconds, ok ? null : "empty dialog line", Truncate(response));
    }

    private static ProbeResult ValidateInjection(ProbeCase probeCase, string response, Stopwatch stopwatch)
    {
        stopwatch.Stop();

        using var document = JsonContractParser.TryParseDocument(response, out var error);
        if (document is null)
        {
            return new ProbeResult(probeCase.Id, probeCase.Name, probeCase.Kind, false, stopwatch.ElapsedMilliseconds, error, null);
        }

        var echo = document.RootElement.TryGetProperty("echo", out var property) &&
                   property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

        var ok = echo is not null && echo.Contains(ProbeCases.InjectionPayload, StringComparison.Ordinal);
        return new ProbeResult(
            probeCase.Id,
            probeCase.Name,
            probeCase.Kind,
            ok,
            stopwatch.ElapsedMilliseconds,
            ok ? null : "the injected text was not preserved as data",
            ok ? null : Truncate(response));
    }

    /// <summary>Removes the final character so a balanced JSON document becomes malformed.</summary>
    private static string Corrupt(string text)
    {
        var trimmed = JsonContractParser.TrimFences(text).Trim();
        return trimmed.Length == 0 ? "{ \"broken\": " : trimmed[..^1];
    }

    private static string Truncate(string text, int max = 200) =>
        text.Length <= max ? text : text[..max] + "…";
}
