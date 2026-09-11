using System.Text.Json;
using FluentAssertions;
using LifeSim.AI.Diagnostics;
using LifeSim.Core.Diagnostics;
using Microsoft.Extensions.AI;
using Xunit;

namespace LifeSim.AI.Tests;

public class RecordingChatClientTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"lifesim-record-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetResponseAsync_WritesExactlyOneRecord_WithCorrelationAndMetadata()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);
        var inner = new FakeChatClient("hello world");
        var client = new RecordingChatClient(inner, recorder, agent: "Narrator");

        ChatResponse response;
        using (TurnCorrelation.Begin("turn-42"))
        {
            response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                new ChatOptions { ModelId = "test-model" });
        }

        response.Text.Should().Be("hello world");

        var records = ReadRecords();
        records.Should().ContainSingle();
        var record = records[0];
        record.CorrelationId.Should().Be("turn-42");
        record.Agent.Should().Be("Narrator");
        record.Model.Should().Be("test-model");
        record.Success.Should().BeTrue();
        record.Response.Should().Be("hello world");
        record.Request.Should().ContainSingle().Which.Content.Should().Be("hi");
        record.PromptHash.Should().HaveLength(64);
        record.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetResponseAsync_MintsCorrelation_WhenNoScopeActive()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);
        var client = new RecordingChatClient(new FakeChatClient("ok"), recorder);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var record = ReadRecords().Single();
        record.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetResponseAsync_UsesAgentFromChatOptions_WhenNotProvidedExplicitly()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);
        var client = new RecordingChatClient(new FakeChatClient("ok"), recorder);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [RecordingChatClient.AgentPropertyKey] = "Director",
            },
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        ReadRecords().Single().Agent.Should().Be("Director");
    }

    [Fact]
    public async Task GetResponseAsync_RecordsFailure_AndRethrows()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);
        var inner = new FakeChatClient().EnqueueThrow(new InvalidOperationException("boom"));
        var client = new RecordingChatClient(inner, recorder);

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var record = ReadRecords().Single();
        record.Success.Should().BeFalse();
        record.Error.Should().Be("boom");
        record.Response.Should().BeNull();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WritesSingleAggregatedRecord()
    {
        using var recorder = new JsonlLlmCallRecorder(_directory);
        var client = new RecordingChatClient(new FakeChatClient("stream me"), recorder);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            chunks.Add(update.Text);
        }

        chunks.Should().Equal("stream me");
        var record = ReadRecords().Single();
        record.Success.Should().BeTrue();
        record.Response.Should().Be("stream me");
    }

    private List<LlmCallRecord> ReadRecords()
    {
        if (!File.Exists(Path.Combine(_directory, "llm-calls.jsonl")))
        {
            return [];
        }

        return File.ReadAllLines(Path.Combine(_directory, "llm-calls.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<LlmCallRecord>(line)!)
            .ToList();
    }
}
