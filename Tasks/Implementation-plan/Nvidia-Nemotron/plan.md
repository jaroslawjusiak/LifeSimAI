# Implementation Plan – Life‑Simulation Game Engine  

**Technology stack** – .NET 10 (C#), Spectre.Console, local Jan LLM API, open‑source multi‑agent framework (e.g., **AutoGen.NET** or **Semantic Kernel** agents).  
**Deployment** – Purely local console application (no web / cloud).  

The plan is broken into **milestones**, each containing **user stories** (US) and, where useful, **sub‑tasks** (ST).  
Every story / sub‑task is prefixed to indicate its primary concern:

* **[CS]** – Console‑application / UI / core engine mechanics (input, turn loop, state persistence, packaging, etc.)  
* **[AI]** – Anything that touches the LLM / multi‑agent system (prompt design, agent registration, response parsing, action translation, safety/guardrails, etc.)  

The description follows the classic *As a …, I want …, so that …* format and includes **acceptance criteria**, **edge‑case notes**, and **estimated effort** (in story points – 1 pt ≈ ½ day for a solo developer). Feel free to adjust the points to your velocity.

---

## 📦 Milestone 0 – Project Kick‑off & Foundations  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M0‑CS‑US‑001 | [CS] | **Create solution skeleton** | Set up a .NET 10 console project, add Spectre.Console, configure Git repo, add README with build/run instructions. | `dotnet run` launches a blank console app that prints “Welcome to the Life‑Sim Engine”. | 2 |
| M0‑CS‑US‑002 | [CS] | **Define coding standards & CI** | Add EditorConfig, .NET stylecop analyzers, minimal GitHub Actions workflow that builds & runs unit tests on push/PR. | Build passes on CI; no analyzer warnings. | 1 |
| M0‑AI‑US‑003 | [AI] | **Survey local LLM options** | Jan server (or alternative like Ollama) installed locally; test a simple prompt/response via HTTP. | Able to POST a JSON prompt to `http://localhost:1337/v1/completions` and receive a text response. | 2 |
| M0‑AI‑US‑004 | [AI] | **Select multi‑agent framework** | Evaluate AutoGen.NET, Semantic Kernel, or hand‑rolled agent bus; pick the one with smallest dependency footprint and clear C# API. | Decision documented; a minimal “ping‑pong” agent demo works. | 3 |

**Milestone 0 Goal** – A clean, compilable solution with linting, CI, and a verified local LLM + agent‑bus ready for integration.

---

## 🏗️ Milestone 1 – Core Engine & Time System  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M1‑CS‑US‑005 | [CS] | **Design Engine Core interfaces** | Define `IGameEngine`, `IGameState`, `ITimeProvider`, `IStatManager`. Core responsibilities: tick, pause, save/load. | Interfaces compile; unit‑testable mocks can be injected. | 3 |
| M1‑CS‑ST‑006 | [CS] | **Implement TimeProvider** | Handles Gregorian calendar, day‑of‑week, hour‑minute resolution, configurable ticks per minute (e.g., 1 tick = 1 min). Supports pause, speed‑up (1x, 2x, 5x). | - Time advances correctly on each tick.<br>- Pause/resume works.<br>- Speed change updates tick interval.<br>- Edge: overflow at year 9999 throws `InvalidOperationException`. | 4 |
| M1‑CS‑ST‑007 | [CS] | **Implement StatManager** | Generic stat container (energy, hunger, mood, money, health, etc.) with min/max, regeneration rates, and modifiers. Supports additive & multiplicative modifiers that expire after a duration. | - Stats can be queried/updated.<br>- Regeneration applied each tick based on rates.<br>- Modifier expiry works.<br>- Edge: stat never goes below min or above max (clamped). | 5 |
| M1‑CS‑US‑008 | [CS] | **Game Loop skeleton** | While (!engine.IsOver) { engine.ProcessInput(); engine.Update(TimeSpan.FromTicks(1)); engine.Render(); } | Loop runs at target FPS (default 30) when not paused; can be stopped via ESC. | 3 |
| M1‑CS‑US‑009 | [CS] | **Persist/Load game state** | JSON serialization of `IGameState` (time, stats, world snapshot). Provide `SaveGame(string path)` and `LoadGame(string path)`. | Saved file can be re‑loaded and yields identical state (deterministic). | 4 |
| M1‑CS‑ST‑010 | [CS] | **Handle edge‑cases in persistence** | - Corrupt file → graceful error & fallback to new game.<br>- Versioning: add a schema version field; migration stub for future changes. | - Corrupt JSON throws descriptive error, engine returns to main menu.<br>- Version 2 load works after version‑bump. | 2 |

**Milestone 1 Goal** – A deterministic, time‑driven core that can tick, manage stats, pause/resume, speed‑change, and persist state.

---

## 📚 Milestone 2 – World Definition & Markdown Parsing  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M2‑CS‑US‑011 | [CS] | **Define World Data Model** | Classes: `World`, `Location`, `Character (NPC/Player)`, `Trait`, `Action`, `Dialogue`, `Quest`. Relationships: World contains Locations; Locations contain Characters & Items; Characters have Stats, Traits, and a list of possible Actions. | Model can be instantiated and serialized to JSON without circular reference errors (use `[JsonIgnore]` or DTOs). | 5 |
| M2‑CS‑US‑012 | [CS] | **Markdown schema design** | Decide on a lightweight markdown‑front‑matter format (YAML front matter + body). Example sections: `world`, `locations`, `characters`, `traits`, `actions`, `dialogues`. Provide a sample markdown file. | Sample file validates against schema (unit test using a JSON‑schema validator). | 3 |
| M2‑CS‑ST‑013 | [CS] | **Build Markdown parser** | Use a markdown parser (e.g., **Markdig**) to extract front matter; deserialize YAML into the model via **YamlDotNet**. Provide fallback defaults for missing fields. | - All sample data loads correctly.<br>- Missing optional fields use defaults (e.g., trait value = 0).<br>- Invalid YAML throws clear `WorldLoadException`. | 6 |
| M2‑CS‑US‑014 | [CS] | **World Loader service** | `IWorldLoader.Load(string markdownPath) → World`. Caches parsed worlds in‑memory for hot‑reload during dev. | Loader returns same object for same path within same process (cache hit). | 3 |
| M2‑CS‑ST‑015 | [CS] | **Edge‑case handling in loader** | - Duplicate IDs → error with offending ID.<br>- Circular references (e.g., location A → B → A) → detect and break with warning.<br>- Missing required fields → validation error. | All edge cases produce deterministic errors; engine can continue with a safe empty world. | 4 |
| M2‑CS‑US‑016 | [CS] | **Integrate world into engine** | On engine start, load a selected world (via command line arg or menu) and inject it into `IGameState`. | Engine’s `CurrentWorld` property is non‑null after start; time and stats are attached to the player character defined in world. | 3 |

**Milestone 2 Goal** – Ability to describe any game world entirely in markdown, load it into a strongly‑typed object graph, and plug it into the engine.

---

## 🤖 Milestone 3 – Multi‑Agent AI Infrastructure  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M3‑AI‑US‑017 | [AI] | **Define Agent Contracts** | Interfaces: `IAgent`, `IActionAgent` (generates raw LLM text), `IActionTranslator` (converts LLM output → structured `GameAction`). Agents have a name, role, and access to a shared `Blackboard` (world snapshot). | Contracts compile; can be injected via DI. | 2 |
| M3‑AI‑ST‑018 | [AI] | **Setup Jan client wrapper** | Thin HttpClient wrapper (`JanLlmClient`) with methods: `CompleteAsync(prompt, temperature, maxTokens)`. Handles retries, timeout, and logs request/response. | - Successful call returns text.<br>- On 5xx or network error, throws `LlmServiceException` after 3 retries. | 4 |
| M3‑AI‑ST‑019 | [AI] | **Register core agents** | - **WorldNarratorAgent** (`IActionAgent`): produces location/character descriptions.<br>- **DialogueAgent** (`IActionAgent`): generates NPC lines given context.<br>- **ActionTranslatorAgent** (`IActionTranslator`): maps free‑form text to a predefined `ActionType` (e.g., “Work”, “Talk”, “Rest”) + parameters. | Each agent can be instantiated and called with a dummy prompt; returns expected type. | 6 |
| M3‑AI‑US‑020 | [AI] | **Prompt engineering library** | Create static class `PromptBuilder` with methods like `BuildWorldDescriptionPrompt(WorldSnapshot)`, `BuildDialoguePrompt(NPC, PlayerContext)`, `BuildActionTranslationPrompt(LLMResponse)`. Uses string interpolation and token‑count estimation to stay within model limits. | Unit tests verify prompts are < 80 % of model’s max tokens for typical snapshots. | 4 |
| M3‑AI‑ST‑021 | [AI] | **Safety & Guardrails** | Implement profanity filter, prompt‑injection detection (simple regex for “ignore previous instructions”), and response length caps. Log any blocked attempts. | - Injected prompt returns safe fallback.<br>- Profanity replaced with “[censored]”. | 3 |
| M3‑AI‑US‑022 | [AI] | **Agent Bus / Orchestration** | Simple orchestrator (`AgentOrchestrator`) that, each tick, calls relevant agents based on current game state (e.g., if player enters a location → WorldNarratorAgent; if NPC present → DialogueAgent). Results are posted to a thread‑safe `ConcurrentQueue<AgentResult>` for the engine to consume. | Orchestrator can be started/stopped; agent calls do not block the main game loop (use `Task.Run` with configurable max concurrency). | 5 |
| M3‑AI‑ST‑023 | [AI] | **Translate agent output to game actions** | `ActionTranslatorAgent` returns a `GameAction` DTO (`ActionType`, `TargetId`, `Parameters`). Engine validates action against world rules before applying. | - Valid actions update stats/world.<br>- Invalid actions are rejected and logged; engine may ask player to clarify. | 5 |
| M3‑AI‑US‑024 | [AI] | **Testing agent interactions** | Write integration tests that spin up a fake Jan server (e.g., using **WireMock.Net**) to return canned responses; verify end‑to‑end flow from prompt → action → state change. | Tests pass for at least three scenarios: description generation, dialogue, action translation. | 6 |

**Milestone 3 Goal** – A loosely‑coupled, testable multi‑agent pipeline that can take a world snapshot, ask the LLM for narrative/dialogue, and turn the LLM’s free‑form answer into concrete game actions.

---

## 🖥️ Milestone 4 – Console UI & Interaction (Spectre.Console)  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M4‑CS‑US‑025 | [CS] | **Main menu screen** | Spectre.Console `SelectionPrompt` with options: New Game, Load Game, Settings, Quit. | Navigation works; selecting New Game prompts for world selection. | 3 |
| M4‑CS‑ST‑026 | [CS] | **World selector** | Reads `./Worlds/` folder for `.md` files, displays them in a table with name/description (extracted from front matter). User picks one; engine loads it. | Correct world loads; invalid file shows error and returns to menu. | 4 |
| M4‑CS‑US‑027 | [CS] | **In‑game HUD** | Top‑bar shows: Day HH:MM, Energy/Hunger/Mood/Money (progress bars), Location name. Bottom‑bar shows available actions (numeric shortcuts). Updates each tick via `Live` or `AnsiConsole.Status`. | HUD refreshes smoothly (< 100 ms lag); progress bars reflect current stats. | 5 |
| M4‑CS‑ST‑028 | [CS] | **Action prompt** | When it’s the player’s turn, show a `SelectionPrompt` of possible actions (derived from player’s location, stats, and NPCs). Allow free‑text input for “custom” actions that go to the ActionTranslatorAgent. | - Selecting an action executes it immediately.<br>- Free‑text triggers agent pipeline and shows LLM reasoning (optional debug mode). | 4 |
| M4‑CS‑US‑029 | [CS] | **Dialogue display** | When an NPC speaks, render their name in a distinct color, wrap text, and optionally show a typing animation. | Dialogue appears clearly; long text scrolls if needed. | 3 |
| M4‑CS‑ST‑030 | [CS] | **Settings screen** | Adjust game speed (1x, 2x, 5x), enable/disable AI narration, set LLM temperature, toggle debug logs. Persists to `settings.json`. | Changes take effect immediately; settings survive restart. | 3 |
| M4‑CS‑US‑031 | [CS] | **Save/Load in‑game** | Press **S** to quick‑save (auto‑named with timestamp), **L** to list saves, **Enter** to load selected. | Save/load works without breaking the current tick; loading restores exact HUD state. | 4 |
| M4‑CS‑ST‑032 | [CS] | **Error handling & crash safety** | Unhandled exceptions show a red error panel, offer to quit or return to main menu; log full stack to `logs/error.log`. | Engine never terminates abruptly; user gets a friendly message. | 3 |
| M4‑CS‑US‑033 | [CS] | **Accessibility & ANSI fallback** | Detect if terminal lacks color support; switch to monochrome mode. Provide an option to increase font size via scaling (if supported). | In monochrome mode, all information is still readable via symbols or bold text. | 2 |

**Milestone 4 Goal** – A polished, responsive console UI built with Spectre.Console that lets the player navigate worlds, see stats, choose actions, and interact with AI‑driven NPCs.

---

## 📦 Milestone 5 – Game‑World Package System (NuGet‑like)  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M5‑CS‑US‑034 | [CS] | **Define package format** | A `.lifesim` zip file containing: <br>• `world.md` (main world description) <br>• optional `assets/` (images, audio – not used by engine but available for front‑ends) <br>• `metadata.json` (name, version, author, dependencies). | Package can be created via `dotnet pack`‑style command (`lifesim pack <folder>`) and yields a valid zip. | 4 |
| M5‑CS‑ST‑035 | [CS] | **Package resolver / local feed** | Implement `IPackageProvider` that scans a local folder (`./Packages/`) for `.lifesim` files, reads metadata, and exposes `ListAvailable()`, `Install(string id, string version)`. | Installing a package extracts it to `./InstalledWorlds/<id>_<v>/` and makes it selectable from the world selector. | 5 |
| M5‑CS‑US‑036 | [CS] | **Versioning & dependency resolution** | Simple semantic versioning; if a package declares a dependency on another package (e.g., a “fantasy‑traits” pack), the resolver installs dependencies automatically. | Installing a pack with dependencies pulls in the required packs; conflicts cause a clear error dialog. | 4 |
| M5‑CS‑ST‑037 | [CS] | **Integrate package system with world selector** | World selector now shows both raw `.md` files and installed packages (displaying package name + version). Selecting a package loads its `world.md`. | User can install a new world pack, then immediately play it from the menu. | 3 |
| M5‑CS‑US‑038 | [CS] | **CLI tool for package management** | Add a sub‑command `lifesim pkg` with sub‑commands `list`, `install <id> [version]`, `uninstall <id>`, `update`. | CLI works from a fresh checkout; `lifesim pkg install fantasy-core` downloads (or copies) the pack and makes it available. | 4 |
| M5‑CS‑ST‑039 | [CS] | **Security & integrity checks** | Compute SHA‑256 hash of package on download (if ever extending to a network feed) and verify against metadata; for local feed just ensure file is not tampered (optional). | Tampered package fails to install and logs warning. | 2 |
| M5‑CS‑US‑040 | [CS] | **Documentation & sample packs** | Create two sample worlds (modern‑life, fantasy‑kingdom) as `.lifesim` packs and place them in the repo’s `Samples/` folder. Add a README explaining how to build/pack a new world. | Sample packs load and run without modification; README is clear. | 3 |

**Milestone 5 Goal** – Users can share and install game worlds like NuGet packages, completely offline, with versioning and basic dependency handling.

---

## 🧪 Milestone 6 – Testing, Validation & Edge‑Case Coverage  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M6‑CS‑US‑041 | [CS] | **Unit test core engine** | Test `TimeProvider`, `StatManager`, `GameEngine` tick logic with mocked worlds. Aim for > 80 % line coverage on core. | All tests pass on CI; coverage report shows target met. | 6 |
| M6‑AI‑US‑042 | [AI] | **Mock LLM integration tests** | Use **WireMock.Net** or **Microsoft.AspNetCore.Mvc.Testing** to fake Jan endpoint; verify agent prompts, retries, and action translation. | Tests cover happy path, timeout, malformed JSON, and profanity filtering. | 5 |
| M6‑CS‑ST‑043 | [CS] | **Property‑based testing (FsCheck / NetOrigami)** | Generate random world definitions (within schema) and assert that load → save → load yields identical state (ignoring timestamps). | No exceptions; state equality holds. | 4 |
| M6‑CS‑ST‑044 | [CS] | **Stress test time speed** | Run engine at 5x speed for 10 simulated days with many NPCs (≈ 200) and verify that tick time stays < 50 ms per frame on target hardware. | Performance stays within budget; no deadlocks. | 3 |
| M6‑CS‑US‑045 | [CS] | **Regression test suite for UI** | Spectre.Console “test console” (using `TestHost`) to assert that menu selections produce expected state changes. | UI test suite passes on each PR. | 3 |
| M6‑CS‑ST‑046 | [CS] | **Load‑save compatibility across versions** | Save a game with version 1 schema, then upgrade engine to version 2 (add new stat) and load; verify migration works and defaults are applied. | Backward/forward compatibility verified. | 3 |
| M6‑CS‑US‑047 | [CS] | **Game‑world validation suite** | Run the markdown parser against a corpus of valid and invalid markdown files; ensure proper errors are thrown. | All valid files load; invalid files give helpful messages. | 3 |
| M6‑AI‑ST‑048 | [AI] | **Agent concurrency safety** | Run many simultaneous agent calls (e.g., 20 NPCs requesting dialogue) and assert no race conditions in shared blackboard or LLM client. | No exceptions; all responses returned. | 3 |

**Milestone 6 Goal** – High confidence that the engine behaves correctly under normal, edge, and stress conditions; CI blocks merges that break tests or coverage.

---

## 📚 Milestone 7 – Documentation, Samples & Release  

| ID | Type | Title | Description | Acceptance Criteria | Effort |
| ---- | ------ | ------- | ------------- | ---------------------- | -------- |
| M7‑CS‑US‑049 | [CS] | **Engine API reference** | Generate XML docs for public interfaces and publish as `docs/api.md` using DocFX or similar. | Navigation from README to API is possible; all public members documented. | 4 |
| M7‑CS‑US‑050 | [CS] | **User guide – Creating a world** | Step‑by‑step tutorial: folder structure, markdown schema, sample world, how to test locally, how to pack. | New user can create a working world from scratch in ≤ 30 min. | 5 |
| M7‑CS‑US‑051 | [CS] | **AI guide – Prompt engineering & agent customization** | Explain how to add a new agent, modify prompts, tune temperature, and use the built‑in safety filters. | Contributor can add a simple “quest‑generator” agent and see it work. | 4 |
| M7‑CS‑ST‑052 | [CS] | **Release packaging** | Create a self‑contained `dotnet publish` script that outputs a single‑folder executable (win‑x64, linux‑x64, osx‑arm64). Include a `README` with how to run. | Running the published exe on a clean machine works out‑of‑the‑box (requires only Jan server). | 3 |
| M7‑CS‑US‑053 | [CS] | **Changelog & versioning** | Adopt Keep a Changelog format; bump version after each milestone. | `CHANGELOG.md` reflects all completed work. | 2 |
| M7‑CS‑US‑054 | [CS] | **License & contribution guidelines** | Add MIT license, `CONTRIBUTING.md` with coding standards, PR process, and how to report issues. | Repository is ready for external contributors. | 2 |

**Milestone 7 Goal** – All necessary documentation and build artifacts are in place; the project can be shipped as a downloadable zip or built from source.

---

# 📊 Summary of Effort (Story Points)

| Milestone | Points |
| ----------- | -------- |
| 0 – Foundations | 8 |
| 1 – Core Engine & Time | 20 |
| 2 – World Definition & Markdown | 21 |
| 3 – Multi‑Agent AI | 26 |
| 4 – Console UI (Spectre.Console) | 27 |
| 5 – Package System | 21 |
| 6 – Testing & Validation | 28 |
| 7 – Docs & Release | 20 |
| **Total** | **171 pts** |

*(Assuming 1 pt ≈ ½ day → ~85 working days ≈ 4 months for a solo developer. Adjust according to your velocity or team size.)*

---

## 🛠️ How to Use This Plan  

1. **Create a Git repo** and add the milestone folders (`Milestone0/`, `Milestone1/`, …) as a reference.  
2. **For each user story**:  
   * Write a brief **spec** (if needed) in the repo’s `docs/specs/`.  
   * Break the story into the listed sub‑tasks (or further decompose).  
   * Implement, write unit/integration tests, then **mark as Done** when acceptance criteria are satisfied.  
3. **Continuous Integration**:  
   * Use the CI workflow from Milestone 0 to build, run tests, and compute coverage after each PR.  
   * Enforce a minimum coverage (e.g., 75 % overall, 80 % on core engine).  
4. **Feedback loops**:  
   * After Milestone 2, play with a few sample worlds to validate the markdown schema.  
   * After Milestone 3, run the agent‑integration tests with a mock Jan server to confirm LLM‑driven behavior works before pointing at the real Jan instance.  
5. **Release**: When Milestone 7 is complete, tag the repo (e.g., `v1.0.0`), generate the publishable binaries, and attach the release notes from `CHANGELOG.md`.  

---

### 🎉 Final Note  

The plan deliberately separates **concerns** (console/UI, world data, time/stats, AI agents, packaging, testing) while keeping the interfaces simple enough to swap implementations (e.g., replace Spectre.Console with another UI library or swap Jan for a local Ollama server).  

All edge cases mentioned—invalid markup, stat clamping, time overflow, corrupt saves, LLM failures, concurrency, version mismatches—are addressed with explicit acceptance criteria, ensuring that the resulting engine is robust, extensible, and ready for you (or anyone else) to drop in new markdown‑based worlds and start playing immediately. Happy coding!
