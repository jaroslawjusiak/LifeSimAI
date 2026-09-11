namespace LifeSim.Core.Diagnostics;

/// <summary>
/// Ambient per-turn correlation identifier. Minted once per turn and flowed through
/// every pipeline stage so log records and journal entries can be stitched together.
/// </summary>
/// <remarks>
/// Uses <see cref="AsyncLocal{T}"/> so the identifier follows the asynchronous call
/// graph of the current turn without being shared between concurrent turns.
/// </remarks>
public static class TurnCorrelation
{
    private static readonly AsyncLocal<string?> CurrentId = new();

    /// <summary>The correlation identifier of the turn currently executing, if any.</summary>
    public static string? Current => CurrentId.Value;

    /// <summary>Mints a new, unique correlation identifier.</summary>
    public static string Mint() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Begins a correlation scope. When <paramref name="correlationId"/> is null a new
    /// identifier is minted. Disposing the returned scope restores the previous value.
    /// </summary>
    public static IDisposable Begin(string? correlationId = null)
    {
        var previous = CurrentId.Value;
        CurrentId.Value = string.IsNullOrWhiteSpace(correlationId) ? Mint() : correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentId.Value = previous;
        }
    }
}
