---
name: ai-agent-orchestration
description: |
  Use when implementing the LifeSim.AI agent layer: the five agents, the per-turn
  orchestrator, stage delegates with timeouts and fallbacks, context packets and token
  budgets, NPC memory summaries, and strict markdown prompt templates with placeholder
  validation. Complements microsoft-extensions-ai-testing (client) and
  llm-redteam-evaluator (AIGate).
---

# AI Agent Orchestration, Context & Prompt Templates in LifeSim Engine

This skill covers the wiring **around** the LLM client: how agents are composed, grounded,
budgeted and kept from breaking the turn ([ADR-004](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1340)).
The client itself (decorators, structured output/repair) is in
`microsoft-extensions-ai-testing`; the safety gate is in `llm-redteam-evaluator`.

---

## 1. Principles

1. **An agent is data, not a class hierarchy:** `{ prompt template + model config + typed
   output parser + rule-based fallback }` composed by a small in-process orchestrator.
2. **Failure never fails the turn.** Any stage error selects that stage's fallback
   ([M5-06](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L795)).
3. **Ground, don't dump.** Agents receive a compact `ContextPacket`, never raw chat history.
4. **Pinned facts never evict.** Budgeting may drop rolling events and re-summarise; it may
   never drop pinned (clock, location, player stats, goals, arc state).
5. **Prompts are content.** Templates are markdown with `{{Placeholder}}` / `{{#Section}}`
   and are sandboxed to a fixed placeholder inventory.

Stage order: `Translate → Validate(engine) → Apply(engine) → WorldTick(+Director) → Narrate → Options → Gate → Render`.

---

## 2. Stage Delegates & Orchestrator

```csharp
public interface ITurnStage<in TIn, TOut>
{
    string Name { get; }
    Task<TOut> ExecuteAsync(TIn input, TurnContext context, CancellationToken ct);
}

public sealed record AgentTimeouts(
    TimeSpan Translator, TimeSpan Narrator, TimeSpan Options, TimeSpan Npc, TimeSpan Director);
```

```csharp
public sealed class TurnOrchestrator(
    IAgentKillSwitches kill,
    AgentTimeouts timeouts,
    IAiStatus status,
    Func<PlayerInput, TurnContext, CancellationToken, Task<TranslatedAction>> translate,
    Func<TranslatedAction, TurnContext, CancellationToken, Task<ActionResult>> apply,
    Func<TurnContext, CancellationToken, Task<Narration>> narrate,
    Func<TurnContext, CancellationToken, Task<IReadOnlyList<ActionOption>>> options)
{
    public async Task<TurnResult> RunAsync(PlayerInput input, TurnContext context, CancellationToken ct)
    {
        var translated = await RunStageAsync(
            "translate", timeouts.Translator,
            stage: c => translate(input, context, c),
            fallback: () => TranslatedAction.ToMenuFallback(input),
            ct);

        // Engine stages are not AI and must never be skipped.
        var result = await apply(translated, context, ct);

        var narration = await RunStageAsync(
            "narrate", timeouts.Narrator,
            stage: c => narrate(context, c),
            fallback: () => Narration.FromOutcome(result.Diff),
            ct);

        var opts = await RunStageAsync(
            "options", timeouts.Options,
            stage: c => options(context, c),
            fallback: () => RuleBasedOptions.Generate(context),
            ct);

        return TurnResult.Completed(result, narration, opts);
    }

    private async Task<T> RunStageAsync<T>(
        string name, TimeSpan timeout,
        Func<CancellationToken, Task<T>> stage,
        Func<T> fallback,
        CancellationToken ct)
    {
        if (kill.IsDisabled(name) || !status.IsOnline)
            return fallback();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await stage(cts.Token);
        }
        catch (LlmError)
        {
            return fallback(); // typed failure, never crosses the UI boundary
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return fallback(); // stage timeout, not a user cancel
        }
    }
}

public sealed record AgentKillSwitches(bool Translator, bool Narrator, bool Options, bool Npc, bool Director)
    : IAgentKillSwitches;
```

---

## 3. Context Packets & Token Budgeting ([M4-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L640))

```csharp
public sealed record ContextPacket(
    PinnedFacts Pinned,
    IReadOnlyList<JournalEntry> Rolling,
    IReadOnlyDictionary<string, string> NpcSummaries);

public sealed record PinnedFacts(
    GameClock Clock,
    string LocationId,
    IReadOnlyList<string> NpcIdsPresent,
    StatSnapshot PlayerStats,
    IReadOnlyList<string> ActiveGoals,
    IReadOnlyDictionary<string, string> ArcStates);
```

```csharp
public sealed class TokenBudgeter(int maxTokens)
{
    private const int CharsPerToken = 4;
    private const string OverflowMarker = "…[earlier events summarised]";

    public int BudgetChars => maxTokens * CharsPerToken;

    public ContextPacket Fit(ContextPacket packet, string renderedPinned)
    {
        var used = renderedPinned.Length;
        var kept = new List<JournalEntry>();

        // Evict oldest rolling entries first; pinned is never touched.
        foreach (var e in packet.Rolling.OrderByDescending(e => e.Seq))
        {
            var size = Render(e).Length;
            if (used + size > BudgetChars) break;
            used += size;
            kept.Add(e);
        }

        kept.Reverse();
        return packet with { Rolling = kept };
    }

    private static string Render(JournalEntry e) =>
        $"#{e.Seq} d{e.SimTime.DayIndex} {e.Type}: {e.Payload}";
}
```

**NPC memory summary** is updated after each scene and is part of the save file:
LLM-summarise when online, extractive fallback when offline ([M5-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L759)).

```csharp
public interface INpcMemoryStore
{
    string Get(string npcId);
    Task UpdateAfterSceneAsync(string npcId, IReadOnlyList<ScrollTurn> scene, CancellationToken ct);
}
```

---

## 4. Strict Prompt Template Engine ([M4-05](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L658))

Templates are markdown with `{{Placeholder}}` and `{{#Section}}…{{/Section}}` — **no logic
beyond sections**. Missing placeholder data is a **world-load error**, caught by the
validator (see `markdown-ast-validator`), not a silent empty string at runtime.

```csharp
public sealed class PromptTemplate
{
    private readonly string _source;
    private readonly IReadOnlySet<string> _placeholders;

    private PromptTemplate(string source, IReadOnlySet<string> placeholders)
    {
        _source = source;
        _placeholders = placeholders;
    }

    public static PromptTemplate Parse(string markdown, IReadOnlySet<string> allowed)
    {
        var found = PlaceholderRegex().Matches(markdown)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = found.Except(allowed).ToArray();
        if (unknown.Length > 0)
            throw new PromptTemplateException($"Unknown placeholders: {string.Join(", ", unknown)}");

        return new PromptTemplate(markdown, found);
    }

    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var missing = _placeholders.Except(values.Keys).ToArray();
        if (missing.Length > 0)
            throw new PromptTemplateException($"Missing values: {string.Join(", ", missing)}");

        var output = SectionRegex().Replace(_source, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : string.Empty);

        return PlaceholderRegex().Replace(output, m => values[m.Groups[1].Value]);
    }

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9_]*)\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\{\{#([A-Za-z][A-Za-z0-9_]*)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline)]
    private static partial Regex SectionRegex();
}
```

Resolution order per agent: `world prompts/<agent>.md` → embedded engine default.
Ship engine defaults as embedded resources so worlds can override but never remove them.

---

## 5. Agent Contracts (summary)

| Agent      | Temp | Timeout | Output                                    | Fallback                        |
| ---------- | ---- | ------- | ----------------------------------------- | ------------------------------- |
| Translator | 0.1  | 25s     | `{ action, targetRef?, params?, confidence }` | grouped action menu / clarify |
| Options    | 0.6  | 30s     | `[{ label, actionType, targetRef?, … }]`  | rule-based legal-action ranker  |
| Narrator   | 0.7  | 20s     | prose block                               | outcome formatter (M3-08)       |
| NPC        | 0.8  | 30s     | `{ line, mood, intentTag, suggestedRelDelta }` | mood-keyed canned lines    |
| Director   | 0.4  | 30s     | `[{ type, ref, params, narrativeHint }]`  | silent deterministic tick       |

Every agent payload passes the `AIGate` before touching state — see `llm-redteam-evaluator`.
Every call is journaled under the turn correlation id — see `microsoft-extensions-ai-testing`.

---

## 6. Telemetry & Kill Switches ([M9-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1161))

- Record per-stage `{ name, latencyMs, status, fallbackUsed }` for the debug overlay (F12).
- Kill switches are config/debug flags that disable a single agent without recompiling.
- Streaming is **off by default**; enable only for the Narrator behind a config flag.

---

## 7. Related Skills

- `microsoft-extensions-ai-testing` — client decorators, structured output, repair, fakes.
- `llm-redteam-evaluator` — AIGate, whitelist, clamping, injection fixtures.
- `spectre-console-tui` — status spinners ("Mia is thinking…") and the debug overlay.
- `world-prompt-authoring` — writing grounded templates and character sheets.
