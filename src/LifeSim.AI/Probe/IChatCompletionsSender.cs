namespace LifeSim.AI.Probe;

/// <summary>
/// Minimal seam over the OpenAI-compatible chat-completions endpoint, so the probe runner is
/// testable without a network. Implemented by <see cref="ProbeSender"/> in production.
/// </summary>
public interface IChatCompletionsSender
{
    /// <summary>Returns the assistant text content for the given messages.</summary>
    Task<string> SendAsync(
        IReadOnlyList<ProbeMessage> messages,
        CancellationToken cancellationToken = default);
}
