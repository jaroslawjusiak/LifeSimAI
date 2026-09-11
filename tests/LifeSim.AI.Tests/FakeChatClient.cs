using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace LifeSim.AI.Tests;

/// <summary>
/// Minimal in-memory <see cref="IChatClient"/> for offline tests. Responses are
/// dequeued in order; enqueue a throwing factory to simulate failures.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _responders = new();

    public FakeChatClient(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responders.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
    }

    public int CallCount { get; private set; }

    public List<IReadOnlyList<ChatMessage>> ReceivedMessages { get; } = [];

    public FakeChatClient Enqueue(Func<ChatResponse> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public FakeChatClient EnqueueThrow(Exception exception)
    {
        _responders.Enqueue(() => throw exception);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        ReceivedMessages.Add(messages.ToList());

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("FakeChatClient was called but no response was configured.");
        }

        return Task.FromResult(_responders.Dequeue()());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CallCount++;
        ReceivedMessages.Add(messages.ToList());

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("FakeChatClient was called but no response was configured.");
        }

        var response = _responders.Dequeue()();
        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, message.Text);
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
