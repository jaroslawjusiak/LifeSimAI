using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using LifeSim.Core.Diagnostics;
using Microsoft.Extensions.AI;

namespace LifeSim.AI.Diagnostics;

/// <summary>
/// <see cref="DelegatingChatClient"/> middleware that writes exactly one
/// <see cref="LlmCallRecord"/> per LLM call to an <see cref="ILlmCallRecorder"/>.
/// Streaming calls are aggregated into a single record emitted when the stream ends.
/// </summary>
public sealed class RecordingChatClient : DelegatingChatClient
{
    /// <summary>Key used to read the agent name from <see cref="ChatOptions.AdditionalProperties"/>.</summary>
    public const string AgentPropertyKey = "agent";

    private readonly ILlmCallRecorder _recorder;
    private readonly string? _agent;

    public RecordingChatClient(IChatClient innerClient, ILlmCallRecorder recorder, string? agent = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
        _agent = agent;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = Materialize(messages);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            await RecordAsync(
                messageList,
                options,
                response.Text,
                response.FinishReason?.Value,
                response.ModelId,
                error: null,
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await RecordAsync(
                messageList,
                options,
                response: null,
                finishReason: null,
                model: null,
                error: ex.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = Materialize(messages);
        var stopwatch = Stopwatch.StartNew();
        var builder = new StringBuilder();
        string? finishReason = null;

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messageList, options, cancellationToken))
            {
                builder.Append(update.Text);
                finishReason = update.FinishReason?.Value ?? finishReason;
                yield return update;
            }
        }
        finally
        {
            stopwatch.Stop();
            await RecordAsync(
                messageList,
                options,
                builder.ToString(),
                finishReason,
                model: null,
                error: null,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RecordAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string? response,
        string? finishReason,
        string? model,
        string? error,
        long latencyMs,
        CancellationToken cancellationToken)
    {
        var record = new LlmCallRecord
        {
            CorrelationId = TurnCorrelation.Current ?? TurnCorrelation.Mint(),
            Agent = ResolveAgent(options),
            Model = model ?? options?.ModelId ?? "unknown",
            PromptHash = PromptFingerprint.Compute(messages, options),
            LatencyMs = latencyMs,
            FinishReason = finishReason,
            Success = error is null,
            Error = error,
            Request = messages.Select(m => new LlmMessageRecord(m.Role.Value, m.Text)).ToArray(),
            Response = response,
        };

        await _recorder.RecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveAgent(ChatOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(_agent))
        {
            return _agent;
        }

        if (options?.AdditionalProperties is { } properties &&
            properties.TryGetValue(AgentPropertyKey, out var value))
        {
            return value?.ToString();
        }

        return null;
    }

    private static IReadOnlyList<ChatMessage> Materialize(IEnumerable<ChatMessage> messages) =>
        messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
}
