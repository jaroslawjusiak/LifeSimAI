namespace LifeSim.AI.Diagnostics;

/// <summary>
/// Durable sink for LLM interaction records. Implementations must be safe to call
/// from concurrent turns.
/// </summary>
public interface ILlmCallRecorder
{
    /// <summary>Appends one record to the journal.</summary>
    Task RecordAsync(LlmCallRecord record, CancellationToken cancellationToken = default);
}

/// <summary>A recorder that discards records — used when journaling is disabled.</summary>
public sealed class NullLlmCallRecorder : ILlmCallRecorder
{
    public static NullLlmCallRecorder Instance { get; } = new();

    private NullLlmCallRecorder()
    {
    }

    public Task RecordAsync(LlmCallRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
