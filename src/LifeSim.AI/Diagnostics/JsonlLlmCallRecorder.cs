using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LifeSim.AI.Diagnostics;

/// <summary>
/// Appends LLM interaction records to a rotating <c>llm-calls.jsonl</c> file.
/// Rotation is size-based; old files are pruned to satisfy both a retained-file
/// count and a total directory-size cap.
/// </summary>
public sealed class JsonlLlmCallRecorder : ILlmCallRecorder, IDisposable
{
    /// <summary>Placeholder written in place of sensitive content when redaction is on.</summary>
    public const string RedactedPlaceholder = "<redacted>";

    private const string ActiveFileName = "llm-calls.jsonl";
    private const string RotatedFilePattern = "llm-calls-*.jsonl";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;
    private readonly long _fileSizeLimitBytes;
    private readonly int _retainedFileCount;
    private readonly long _maxDirectoryBytes;
    private readonly bool _redactSensitiveContent;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonlLlmCallRecorder(
        string directory,
        long fileSizeLimitBytes = 1_000_000,
        int retainedFileCount = 5,
        long maxDirectoryBytes = 10_000_000,
        bool redactSensitiveContent = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileSizeLimitBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFileCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDirectoryBytes, 1);

        _directory = directory;
        _fileSizeLimitBytes = fileSizeLimitBytes;
        _retainedFileCount = retainedFileCount;
        _maxDirectoryBytes = maxDirectoryBytes;
        _redactSensitiveContent = redactSensitiveContent;
    }

    /// <summary>Absolute path of the file currently being appended to.</summary>
    public string ActiveFilePath => Path.Combine(_directory, ActiveFileName);

    public async Task RecordAsync(LlmCallRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var effective = _redactSensitiveContent ? Redact(record) : record;
        var line = JsonSerializer.Serialize(effective, SerializerOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            await using (var stream = new FileStream(
                ActiveFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            RotateIfNeeded();
            Prune();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RotateIfNeeded()
    {
        var current = new FileInfo(ActiveFilePath);
        if (!current.Exists || current.Length < _fileSizeLimitBytes)
        {
            return;
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var rotated = Path.Combine(_directory, $"llm-calls-{stamp}.jsonl");
        var suffix = 0;
        while (File.Exists(rotated))
        {
            rotated = Path.Combine(_directory, $"llm-calls-{stamp}-{++suffix}.jsonl");
        }

        File.Move(ActiveFilePath, rotated);
    }

    private void Prune()
    {
        var rotated = new DirectoryInfo(_directory)
            .EnumerateFiles(RotatedFilePattern)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        // Retained-file cap: keep the newest _retainedFileCount rotated files.
        while (rotated.Count > _retainedFileCount)
        {
            rotated[0].Delete();
            rotated.RemoveAt(0);
        }

        // Total directory cap: delete oldest rotated files until under budget.
        long total = rotated.Sum(f => f.Length);
        var active = new FileInfo(ActiveFilePath);
        if (active.Exists)
        {
            total += active.Length;
        }

        var index = 0;
        while (total > _maxDirectoryBytes && index < rotated.Count)
        {
            total -= rotated[index].Length;
            rotated[index].Delete();
            index++;
        }
    }

    private static LlmCallRecord Redact(LlmCallRecord record) => record with
    {
        Request = record.Request
            .Select(m => m with { Content = RedactedPlaceholder })
            .ToArray(),
        Response = record.Response is null ? null : RedactedPlaceholder,
    };

    public void Dispose() => _gate.Dispose();
}
