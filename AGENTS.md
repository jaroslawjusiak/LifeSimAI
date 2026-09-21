# AGENTS.md — LifeSim Engine

Project-wide rules inherited by every opencode agent in this repository.
Agent-specific behaviour lives in `.opencode/agents/*.md`; deep domain knowledge lives in `skills/*/SKILL.md`.

---

## 1. What this is

A **local-first, turn-based life simulation game engine**. C# / .NET 10 console app, Spectre.Console UI,
worlds authored in markdown + YAML frontmatter, local LLM agents over `Microsoft.Extensions.AI`
(Jan at `http://127.0.0.1:1337/v1`). Single player, no deployment, no telemetry, no cloud.

## 2. Source of truth

| Artifact | Role |
| --- | --- |
| `Tasks/Implementation-plan/plan.md` | Scope, story ids (`M3-04`), acceptance criteria, dependencies, ADR-001…ADR-010, DoD. **Authoritative.** |
| `skills/*/SKILL.md` | How to implement each domain. Read the matching skill *before* writing code. |
| `README.md` | Build/run/doctor/probe commands, logging contract. |
| `docs/` | `local-llm-setup.md`, `model-acceptance.md`, `THIRD-PARTY-NOTICES.md`. |

Never invent scope. If a task is not in `plan.md`, say so and get it scoped before building it.

## 3. Non-negotiable rules

1. **One-way dependencies:** `LifeSim.Console` → {`LifeSim.AI`, `LifeSim.World`, `LifeSim.Persistence`} → `LifeSim.Core`. Core references nothing above it and stays **BCL-only** (no `IOptions`, no Serilog, no Microsoft.Extensions.AI). Enforced by architecture tests in `LifeSim.Core.Tests`.
2. **AI proposes, engine disposes** (ADR-006). Model output never mutates state directly — it passes the AIGate: schema validation → RefWhitelist → precondition re-check → delta envelope clamp.
3. **AI-optional.** Every agent stage has a deterministic rule-based fallback. Killing a stage must not fail the turn. Offline mode is complete and winnable.
4. **Determinism.** Seeded RNG, append-only journal. Same seed + same inputs ⇒ identical state hash. No wall-clock, no `Guid.NewGuid()` in domain logic, no unsleeved `DateTime.Now`.
5. **Content in markdown, mechanics in C#.** A second world must ship with **zero** engine changes. No new public engine API without a markdown-level test proving content can drive it.
6. **Central package management.** Versions live only in `Directory.Packages.props`. Never put a `Version=` on a `PackageReference`.
7. **Zero warnings.** `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`. Never silence a warning with a pragma, `NoWarn`, or a nullable suppression — fix the cause.
8. **Tests run offline.** No network, no live LLM, no timing dependence, no test-order dependence. Use `FakeChatClient` and fixed seeds.
9. **Never weaken a test to make it pass.** A failing test is a bug report. Fix the code, or change the test only with an explicit reason stated in the report.
10. **Use ready solutions** (ADR-001/002): Spectre.Console, Markdig, YamlDotNet, Microsoft.Extensions.AI, Polly v8, System.Text.Json, Serilog, xUnit + FluentAssertions. Do not hand-roll equivalents.

## 4. Commands

```bash
dotnet build LifeSim.sln                 # must be warning-free
dotnet test  LifeSim.sln                 # all 5 suites, offline
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj            # play
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- --verbose
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- doctor  # LLM reachability
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- probe   # model acceptance gate
```

Prefer scoping to one project while iterating (`dotnet test tests/LifeSim.Core.Tests/...`), but
**always finish with a full-solution build + test**. A green project with a red solution is not green.

## 5. Skills index — read before acting

| Skill | Use it for |
| --- | --- |
| `adr-plan-workflow` | Story lifecycle, plan.md checkboxes, ADR records, DoD gate. **Read first for any plan work.** |
| `solution-architecture-guardrails` | csproj, project references, `Directory.*.props`, layer purity, architecture tests. |
| `sim-balance-fuzzer` | `LifeSim.Core`: GameClock, StatSet decay/thresholds, ActionResolver, Monte Carlo bots, seeded invariants. |
| `markdown-ast-validator` | `LifeSim.World`: Markdig AST, YamlDotNet strict, non-throwing diagnostics, RefWhitelist, `.lifeworld` packing. |
| `spectre-console-tui` | `LifeSim.Console`: pure-function rendering, HUD, menus, markup escaping, TestConsole snapshots. |
| `microsoft-extensions-ai-testing` | `LifeSim.AI`: DelegatingChatClient pipeline, Polly resilience, JsonSchema.Net structured output, repair loops, FakeChatClient. |
| `ai-agent-orchestration` | `LifeSim.AI`: the 5 agents, per-turn orchestrator, ContextPacket, token budgets, prompt templates. |
| `llm-redteam-evaluator` | AIGate, prompt injection, RefWhitelist, delta clamping, delimiter sandboxing, red-team fixtures. |
| `save-game-persistence` | `LifeSim.Persistence`: versioned JSON saves, atomic tmp→fsync→rename, SHA-256, backups, migrations. |
| `world-prompt-authoring` | World content: character sheets, tone guides, prompt templates, grounding/injection rules. |
| `dotnet-testing-standards` | xUnit/FluentAssertions conventions, golden files, fault injection, 80% Core coverage gate. |

## 6. Agent team

| Agent | Mode | Owns |
| --- | --- | --- |
| `orchestrator` | primary | Classify the task, route to the right specialist, enforce the DoD, report honestly. |
| `architect` | subagent | Layer boundaries, ADRs, dependency direction, API placement, plan structure. |
| `rubber-duck` | subagent | Read-only second opinion. Scores proposals 1–10, names weak spots, offers alternatives. |
| `developer` | subagent | Implements plan stories. Writes failing tests first, then code, then ticks the plan. |
| `debugger` | subagent | Reproduces, triages and fixes build/test/runtime failures. Minimal, root-cause diffs. |
| `testsmith` | subagent | Test design, coverage gate, determinism, fuzz/invariant/soak suites, CI signal. |
| `sentinel` | subagent | AIGate, prompt-injection defence, red-team fixtures, save/config integrity. |
| `worldsmith` | subagent | Markdown/YAML world content, `.lifeworld` packaging, authoring docs. |
| `scribe` | subagent | README, `docs/`, XML doc comments, CHANGELOG, release notes. |

Invoke a subagent directly with `@name` (e.g. `@rubber-duck score this plan`), or just talk to
`orchestrator` and let it route. Details: `.opencode/agents/`.

## 7. Definition of Done (every story, every milestone)

- [ ] All suites green — **including the offline path** (fake client, AI disabled)
- [ ] Fallback proven: kill the stage, the turn still completes
- [ ] Validator clean on both shipped worlds
- [ ] Journal + `llm-calls.jsonl` written for the change's happy path
- [ ] Docs updated: format reference, AI guidelines, or README as applicable
- [ ] No new engine API without a markdown-level test proving content can drive it

## 8. House conventions

- **Paths:** always repository-relative (`src/LifeSim.Core/Stats/StatSet.cs`). Never absolute machine paths.
- **Commits:** reference the story id — `M2-03: entity loaders for locations and characters`.
- **Changelog:** the DoD requires one but `CHANGELOG.md` does not exist yet. Create it on first need using Keep-a-Changelog + SemVer; don't invent back-history.
- **Current state (as of writing):** M0 and M1 are largely implemented (`LifeSim.Core` ~31 files, `LifeSim.AI` ~16, `LifeSim.Console` ~13). `LifeSim.World` and `LifeSim.Persistence` are still skeletons — M2 and M6 are the obvious next work. Verify against `plan.md` rather than trusting this line.
- **No secrets in the repo.** `api-key-generator.ps1` exists, but this is a local-only app — never commit credentials, and never add a cloud LLM provider (explicitly out of scope for 1.0).

## 9. Out of scope for 1.0

Multiplayer or any server component · graphics/audio/non-terminal UI · remote world feeds, signing,
auto-update · cloud LLM providers · tool-use/autonomous agents beyond the five contracts.

If a request implies any of these, stop and flag it instead of building it.
