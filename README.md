# LifeSim Engine

A local-first, turn-based life simulation game engine built as a .NET console application using **Spectre.Console** for UI, markdown + YAML frontmatter for world content, and local AI agents over **Microsoft.Extensions.AI** (e.g. Jan on `http://127.0.0.1:1337/v1`).

---

## Architecture & Project Structure

The solution follows a strict one-way dependency flow: `LifeSim.Console` → {`LifeSim.AI`, `LifeSim.World`, `LifeSim.Persistence`} → `LifeSim.Core`. `LifeSim.Core` references nothing above it.

```
LifeSimAI/
├── src/
│   ├── LifeSim.Core/         # Authoritative engine state, clock, stats, actions, journal, AIGate
│   ├── LifeSim.World/        # Markdown/YAML loaders, validation, RefWhitelist, .lifeworld packaging
│   ├── LifeSim.AI/           # IChatClient pipeline, agents (Translator, Options, Narrator, NPC, Director)
│   ├── LifeSim.Persistence/ # Versioned JSON saves, atomic IO, migrations
│   └── LifeSim.Console/     # Spectre.Console presentation layer, HUD, screens, menus
├── tests/
│   ├── LifeSim.Core.Tests/         # Core domain unit tests + architectural dependency validation
│   ├── LifeSim.World.Tests/        # Markdown parser & world validation tests
│   ├── LifeSim.AI.Tests/           # Agent contracts, repair loops, offline fake client tests
│   ├── LifeSim.Persistence.Tests/  # Save/load, atomic IO & migration tests
│   └── LifeSim.Console.Tests/      # Configuration, HUD & TestConsole snapshot tests
├── skills/                   # Project-scoped developer assistant skills
└── Tasks/
    └── Implementation-plan/  # Detailed milestone implementation specifications
```

---

## Build & Test Instructions

### Prerequisites
- .NET 10 SDK
- PowerShell or bash terminal

### Building the Solution
```bash
dotnet build LifeSim.sln
```

### Running Tests
```bash
dotnet test LifeSim.sln
```

### Running the Application
```bash
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj
```
Add `-- --verbose` to raise console/file log verbosity.

### Checking the local LLM
```bash
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- doctor
```
The `doctor` command probes the configured `Llm:Endpoint` for reachability and confirms the
configured `Llm:Model` is present in `/v1/models`. Exit code `0` = healthy. See
[docs/local-llm-setup.md](docs/local-llm-setup.md) for the full Jan setup runbook.

### Model acceptance gate (structured-output probe)
```bash
dotnet run --project src/LifeSim.Console/LifeSim.Console.csproj -- probe
```
Fires the four probe cases (plain JSON, self-repair, roleplay, injection-as-data) at the
configured model, prints a latency/result table, archives the acceptance record to
`docs/model-acceptance.md`, and exits non-zero when any JSON-contract case fails.

---

## Logging & LLM call journal

- Application events are written to rolling files under `Logging:Directory` (default `~/.lifesim/logs`).
- Every LLM interaction is appended as one JSON line to `llm-calls.jsonl` (fields: `correlationId`, `agent`, `model`, `promptHash`, `latencyMs`, `finishReason`, `success`, `error`, `request`, `response`, `timestampUtc`).
- Rotation is bounded by `Logging:FileSizeLimitBytes`, `Logging:RetainedFileCount` and `Logging:MaxDirectoryBytes`.
- Set `Logging:RedactSensitiveContent=true` to replace prompt/response contents with a placeholder (default off — local single-player app).
