---
name: microsoft-extensions-ai-testing
description: |
  Guides the implementation and unit testing of local LLM integrations using
  Microsoft.Extensions.AI and Polly v8 in LifeSim Engine (LifeSim.AI).
  Covers DelegatingChatClient middleware pipelines (JSONL logging, disk cache,
  resilience), JsonSchema.Net structured output validation, self-repair loops,
  and offline testing with FakeChatClient.
---

# Microsoft.Extensions.AI Pipeline & Testing in LifeSim Engine

This skill guides the construction of the local AI layer ([`LifeSim.AI`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L27)) using **Microsoft.Extensions.AI** in .NET 10, connecting to a local OpenAI-compatible endpoint (e.g., **Jan** on `http://127.0.0.1:1337/v1`).

---

## 1. Architectural Guidelines

1. **Uniform Seam ([`IChatClient`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L588)):** All agents consume `IChatClient`. Never hardcode concrete HTTP clients or SDK clients directly into agent classes.
2. **Middleware Pipeline Pattern:** Build cross-cutting concerns (telemetry, caching, Polly v8 resilience) as chained `DelegatingChatClient` decorators.
3. **Strict Structured Output Contract:** Use `System.Text.Json` combined with `JsonSchema.Net`. Never extract JSON using uncontrolled regex patterns.
4. **Offline Test Suite:** Every agent test must run offline with zero network calls by injecting a mock or `FakeChatClient`.

---

## 2. Decorator / Middleware Implementations

### A. Turn Call Journaling Decorator ([M0-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L109))

Captures every LLM interaction to a rotating `llm-calls.jsonl` file stamped with the turn correlation ID:

```csharp
public sealed class JournalingChatClient(IChatClient inner, string logDirectory) : DelegatingChatClient(inner)
{
    private readonly string _filePath = Path.Combine(logDirectory, "llm-calls.jsonl");

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var messageList = messages.ToList();
        ChatResponse response;

        try
        {
            response = await base.GetResponseAsync(messageList, options, cancellationToken);
            sw.Stop();

            await RecordEntryAsync(new LlmCallRecord(
                CorrelationId: CallContext.CurrentCorrelationId ?? Guid.NewGuid().ToString("N"),
                TimestampUtc: DateTimeOffset.UtcNow,
                LatencyMs: sw.ElapsedMilliseconds,
                Success: true,
                Messages: messageList,
                ResponseText: response.Text,
                Error: null
            ), cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await RecordEntryAsync(new LlmCallRecord(
                CorrelationId: CallContext.CurrentCorrelationId ?? Guid.NewGuid().ToString("N"),
                TimestampUtc: DateTimeOffset.UtcNow,
                LatencyMs: sw.ElapsedMilliseconds,
                Success: false,
                Messages: messageList,
                ResponseText: null,
                Error: ex.Message
            ), cancellationToken);

            throw;
        }
    }

    private async Task RecordEntryAsync(LlmCallRecord record, CancellationToken ct)
    {
        string line = JsonSerializer.Serialize(record) + Environment.NewLine;
        await File.AppendAllTextAsync(_filePath, line, ct);
    }
}
```

---

### B. Content-Hash Disk Cache ([M4-06](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L676))

Replays cached responses during development or re-runs when inputs and model parameters match:

```csharp
public sealed class CachingChatClient(IChatClient inner, string cacheDirectory, bool enabled) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }

        string key = ComputeHash(messages, options);
        string cacheFile = Path.Combine(cacheDirectory, $"{key}.json");

        if (File.Exists(cacheFile))
        {
            string cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            return JsonSerializer.Deserialize<ChatResponse>(cachedJson)!;
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(response), cancellationToken);

        return response;
    }

    private static string ComputeHash(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var payload = new { Messages = messages, Options = options };
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

---

## 3. Structured Output & Self-Repair Loop ([M4-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L622))

Small local models (e.g., Gemma, Qwen 7B/8B) occasionally wrap JSON in explanatory text or miss fields. Use a bounded self-repair loop (maximum 2 attempts) before falling back to rule-based logic:

```csharp
public sealed class StructuredOutputParser<T>(IChatClient chatClient, JsonSchema schema) where T : class
{
    public async Task<Result<T, LlmError>> ParseWithRepairAsync(
        List<ChatMessage> promptMessages,
        ChatOptions options,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>(promptMessages);

        for (int attempt = 0; attempt <= 2; attempt++)
        {
            var response = await chatClient.GetResponseAsync(messages, options, ct);
            string raw = CleanMarkdownFences(response.Text ?? string.Empty);

            // 1. JSON Syntactic Parse
            JsonNode? jsonNode;
            try
            {
                jsonNode = JsonNode.Parse(raw);
            }
            catch (JsonException ex)
            {
                if (attempt == 2) return LlmError.Malformed($"JSON syntax error: {ex.Message}");
                messages.Add(new ChatMessage(ChatRole.Assistant, raw));
                messages.Add(new ChatMessage(ChatRole.User, $"Output was not valid JSON: {ex.Message}. Output JSON only."));
                continue;
            }

            // 2. Schema Validation
            var validation = schema.Evaluate(jsonNode);
            if (!validation.IsValid)
            {
                if (attempt == 2) return LlmError.SchemaViolation(validation.Errors?.ToString() ?? "Schema mismatch");
                messages.Add(new ChatMessage(ChatRole.Assistant, raw));
                messages.Add(new ChatMessage(ChatRole.User, $"JSON does not conform to required schema. Errors: {JsonSerializer.Serialize(validation.Errors)}."));
                continue;
            }

            // 3. Deserialize to Typed Record
            var result = JsonSerializer.Deserialize<T>(raw);
            return result != null ? result : LlmError.Malformed("Null deserialization result");
        }

        return LlmError.ExceededRepairs("Failed to produce valid structured output after 2 repair turns");
    }

    private static string CleanMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];

        if (text.EndsWith("```"))
            text = text[..^3];

        return text.Trim();
    }
}
```

---

## 4. Offline TDD with `FakeChatClient`

Test agents deterministically without requiring a running Jan or Ollama process:

```csharp
public class ActionTranslatorAgentTests
{
    [Fact]
    public async Task Translator_RepairsInvalidJson_OnFirstRetry()
    {
        // Arrange
        var fakeClient = new FakeChatClient(
            "This is not json at all", // 1st attempt: syntax error
            """{"action": "work_shift", "confidence": 0.95}""" // 2nd attempt: valid JSON
        );

        var parser = new StructuredOutputParser<TranslatedAction>(fakeClient, TranslatedAction.Schema);

        // Act
        var result = await parser.ParseWithRepairAsync(
            [new ChatMessage(ChatRole.User, "Go to work")],
            new ChatOptions()
        );

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("work_shift");
        fakeClient.CallCount.Should().Be(2);
    }
}
```
