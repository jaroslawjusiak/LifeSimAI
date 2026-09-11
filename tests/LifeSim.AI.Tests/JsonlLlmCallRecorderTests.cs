using System.Text.Json;
using FluentAssertions;
using LifeSim.AI.Diagnostics;
using Xunit;

namespace LifeSim.AI.Tests;

public class JsonlLlmCallRecorderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"lifesim-journal-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecordAsync_AppendsOneJsonLinePerRecord()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);

        await recorder.RecordAsync(Sample(1));
        await recorder.RecordAsync(Sample(2));

        var lines = File.ReadAllLines(recorder.ActiveFilePath);
        lines.Should().HaveCount(2);
        lines.All(l => JsonSerializer.Deserialize<LlmCallRecord>(l) is not null).Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_RedactsContent_WhenEnabled()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory, redactSensitiveContent: true);

        await recorder.RecordAsync(Sample(1) with
        {
            Response = "top secret",
            Request = [new LlmMessageRecord("user", "top secret")],
        });

        var record = JsonSerializer.Deserialize<LlmCallRecord>(File.ReadAllText(recorder.ActiveFilePath))!;
        record.Response.Should().Be(JsonlLlmCallRecorder.RedactedPlaceholder);
        record.Request.Single().Content.Should().Be(JsonlLlmCallRecorder.RedactedPlaceholder);
        record.PromptHash.Should().Be("hash-1");
    }

    [Fact]
    public async Task RecordAsync_RotatesAndPrunes_ToStayUnderDirectoryCap()
    {
        const long maxDirectoryBytes = 2048;
        using var recorder = new JsonlLlmCallRecorder(
            _directory,
            fileSizeLimitBytes: 256,
            retainedFileCount: 3,
            maxDirectoryBytes: maxDirectoryBytes);

        for (var i = 0; i < 100; i++)
        {
            await recorder.RecordAsync(Sample(i));
        }

        var files = Directory.GetFiles(_directory, "llm-calls*.jsonl");
        var totalBytes = files.Sum(f => new FileInfo(f).Length);

        totalBytes.Should().BeLessThanOrEqualTo(maxDirectoryBytes);
        Directory.GetFiles(_directory, "llm-calls-*.jsonl").Length.Should().BeLessThanOrEqualTo(3);
    }

    private static LlmCallRecord Sample(int index) => new()
    {
        CorrelationId = $"turn-{index}",
        Agent = "Narrator",
        Model = "test-model",
        PromptHash = $"hash-{index}",
        LatencyMs = 12,
        FinishReason = "stop",
        Success = true,
        Request = [new LlmMessageRecord("user", $"prompt {index}")],
        Response = $"response {index}",
    };
}
