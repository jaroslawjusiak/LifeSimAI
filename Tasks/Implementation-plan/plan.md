# LifeSim Engine — Implementation Plan v1.0

Local-first, turn-based **life simulation game engine**. C# / .NET 10 console application
using **Spectre.Console** for UI. Game worlds are authored in **markdown + YAML frontmatter**;
local AI agents (via **Jan**, OpenAI-compatible at `http://127.0.0.1:1337/v1`, accessed over
**Microsoft.Extensions.AI**) control narration, NPCs, options and action translation.
Single player, runs entirely locally — no deployment of any kind.

> Task prefixes: **[CS]** console-application tasks · **[AI]** AI/agent/prompt tasks.
> Effort: S < M < L < XL. Priorities follow MoSCoW (Must / Should / Could).

## Guiding principles

- **Engine owns state** — AI proposes, the engine disposes. All model output passes schema validation, reference whitelists and delta clamps before anything mutates the world.
- **Content in markdown, mechanics in C#** — a second game world ships with zero engine changes.
- **AI-optional** — every agent stage has a deterministic rule-based fallback; offline mode is complete and winnable.
- **Local-first** — one process, files on disk, no accounts, no telemetry.
- **Ready solutions over custom** — Spectre.Console, Markdig, YamlDotNet, Microsoft.Extensions.AI, Polly, System.Text.Json.
- **Determinism first** — seeded RNG, append-only journal, journaled AI I/O; sessions replay to identical state hashes.

## Architecture

```
UI Shell        LifeSim.Console     Spectre screens · HUD · menus · free-text input
Engine Core     LifeSim.Core        clock · stats · actions · turn loop · journal · AIGate
Content Layer   LifeSim.World       markdown loaders · validator · registry · .lifeworld
AI Layer        LifeSim.AI          client · orchestrator · 5 agents · templates · cache
Persistence     LifeSim.Persistence versioned JSON saves · config · logs
```

Per-turn pipeline: `Input → [AI] Translate → Validate → Apply → World tick (+[AI]Director)`
`→ [AI] Narrate → [AI] Dialog → [AI] Options → Gate → Render`

## World package format (.lifeworld)

```
world.yml        manifest: id, name, version, engineVersion, author, defaultPlayer
world.md         overview, tone guide, global rules
locations/*.md   id, connections[], allowedActions[], openHours + prose
characters/*.md  personality, schedule, relationshipStart, voice samples
traits/ skills/ items/ actions/ arcs/ rules/   entity definitions
prompts/*.md     world-level overrides of agent prompt templates
content.sha256   file → hash manifest for tamper detection
```

## Milestones & user stories

### M0 — Foundations & Local AI Toolchain  · _Foundations, ~1 week_

A buildable, testable solution skeleton with pinned dependencies, configuration and logging — plus a verified local LLM environment (Jan) whose structured-output capability is proven before any agent is built.

**Exit gate:**
- dotnet build + dotnet test green on a clean machine (all 5 source + 5 test projects present, zero warnings)
- Probe harness receives schema-valid JSON from the local model on target hardware
- ADR-001…ADR-010 recorded; Jan runbook verified by a second machine or fresh user profile

> **Exit-gate status (2026-09-21): met except the "second machine or fresh user profile" clause.**
> See [`correctness-tracker.md`](correctness-tracker.md) V4 and §5(e).

#### M0-01 [CS] Solution & project scaffolding  · _Must · S_

As a developer, I want a clean multi-project solution so that engine, world loading, AI, persistence and UI evolve independently and stay testable.

**Design notes & edge cases:** Source projects: LifeSim.Core (domain), LifeSim.World (markdown loading), LifeSim.AI (client+agents), LifeSim.Persistence (saves), LifeSim.Console (Spectre UI, entry). Test projects (one per layer that owns behavior): LifeSim.Core.Tests, LifeSim.World.Tests, LifeSim.AI.Tests, LifeSim.Persistence.Tests, LifeSim.Console.Tests. Dependency direction is strictly one-way: Console → {AI, World, Persistence} → Core. Core references nothing above it — and only the BCL, no third-party packages (configuration contracts live in the owning outer layer; see ADR-009). Central props: net10.0, nullable enable, implicit usings, LANG latest.

**Acceptance criteria:**
- dotnet build succeeds for all source + test projects with zero warnings
- Dependency rule enforced (Core references only the BCL and itself — checked by architecture test)
- Directory.Build.props + .editorconfig + .gitignore committed

**Subtasks:**
- [x] [CS] Create LifeSim.sln + 5 src projects with one-way project references
- [x] [CS] Add xUnit + FluentAssertions test projects for Core, World, AI, Persistence, Console
- [x] [CS] Add Directory.Build.props, .editorconfig, README with build steps


#### M0-02 [CS] Dependency baseline & version pinning  · _Must · S_

As a developer, I want third-party libraries chosen and pinned in week one so that integration surprises surface before they can block a milestone.

**Design notes & edge cases:** Spectre.Console (UI), Markdig + YamlFrontMatter extension (markdown), YamlDotNet (frontmatter/manifests), Microsoft.Extensions.AI + Microsoft.Extensions.AI.OpenAI (LLM abstraction — enables a fake client in tests), Polly v8 (resilience), System.Text.Json + JsonSchema.Net (structured output validation), Serilog.Sinks.File or plain rolling writer (logging). Central Package Management via Directory.Packages.props.

**Acceptance criteria:**
- All packages restore on a clean machine with CPM lock file
- Each library spikes successfully in a throwaway command (hello-world per lib)
- LICENSE/notice list generated for the docs folder

**Subtasks:**
- [x] [CS] Add Directory.Packages.props and pin all versions
- [x] [CS] Spike one minimal usage per library and delete the spikes

_Depends on: M0-01_

#### M0-03 [CS] Configuration system  · _Must · S_

As a player, I want one config file (plus env overrides) so I can point the game at my Jan instance and tune model behaviour without recompiling.

**Design notes & edge cases:** appsettings.json shipped with sane defaults (optional: missing file falls back to full defaults); user override at ~/.lifesim/config.json; env vars LIFESIM_* win last. Keys: Llm:Endpoint (default http://127.0.0.1:1337/v1), Llm:Model, per-agent Temperature/MaxTokens/TimeoutSeconds, Ai:Enabled master switch, Paths:Worlds, Paths:Saves, Ui:Theme, Ui:Verbosity. Option contracts live in LifeSim.Console.Configuration (ADR-009); lower layers receive resolved values, never IOptions. Bound via Microsoft.Extensions.Options with data-annotation validation, including explicit validation of each dictionary entry under Llm:Agents:* (property validation does not descend into dictionaries); startup renders an actionable error panel on invalid config.

**Acceptance criteria:**
- Missing config file → full defaults; malformed values → named, actionable error panel
- Per-agent temperature/timeout overrides honored (integration test)
- Env var override wins over file (documented precedence table)

**Subtasks:**
- [x] [CS] Options classes + binding + validation attributes
- [x] [CS] Config precedence chain (defaults → file → user file → env)
- [x] [CS] Startup validation reporter rendered with Spectre panel

_Depends on: M0-01_

#### M0-04 [CS] Logging, tracing & LLM call journal  · _Should · S_

As a developer, I want every prompt/response captured to disk so I can debug agent behavior and later build prompt-regression fixtures.

**Design notes & edge cases:** App events go through Serilog (rolling file sink sized + retained; console sink active only when --verbose); configuration under Logging:* (Directory, FileSizeLimitBytes, RetainedFileCount, MaxDirectoryBytes, RedactSensitiveContent). LLM interactions go to a separate append-only llm-calls.jsonl: exactly one record per call {correlationId, agent, model, promptHash, latencyMs, finishReason, success, error, request, response, timestampUtc}, written by an ILlmCallRecorder behind the IChatClient seam (RecordingChatClient over DelegatingChatClient). Streaming calls aggregate into one record. Rotation is size-based with a retained-file cap and a total directory byte budget. Correlation id is minted per turn (LifeSim.Core.Diagnostics.TurnCorrelation) and flowed through all stages; the M1-06 journal stamps it on entries. Logging contracts live in LifeSim.Console.Configuration; the recorder lives in LifeSim.AI behind an interface (ADR-010).

**Acceptance criteria:**
- Every LLM call produces exactly one JSONL record with the turn correlation id
- Log rotation keeps the log directory under a configured size cap
- Sensitive-content redaction toggle exists (default: off, local app)

**Subtasks:**
- [x] [CS] Rolling file logger with size cap + --verbose flag
- [x] [CS] LLM call recorder (JSONL) hooked behind the IChatClient seam
- [x] [CS] Correlation id minted and flowed per turn (journal stamps it from M1-06)

_Depends on: M0-03_

#### M0-05 [AI] Local LLM environment runbook (Jan)  · _Must · S_

As a developer, I want a documented, repeatable Jan setup so any local machine can host the model the game talks to.

**Design notes & edge cases:** Runbook covers: install Jan → download an instruct model sized to hardware (guidance matrix: 8GB→7B-Q4, 16GB→8–14B-Q4/Q5, 32GB+→larger) → enable Jan's local server (OpenAI-compatible, default port 1337) → verify via curl /v1/chat/completions → set model name in config. Prefers models with strong instruction following / JSON mode behavior. Troubleshooting: port conflicts, GPU offload, context size settings.

**Acceptance criteria:**
- docs/local-llm-setup.md validated walkthrough on a second machine or fresh profile (<30 min to first response)
- Hardware→model matrix with expected tokens/sec recorded
- curl smoke command + expected output included

**Subtasks:**
- [x] [AI] Evaluate 2–3 candidate local models for JSON instruction-following
- [x] [AI] Write the runbook incl. screenshots, hardware matrix, troubleshooting
- [x] [CS] Add `doctor` command that checks endpoint reachability + model presence


#### M0-06 [AI] Prompt capability probe harness  · _Must · M_

As a developer, I want a tiny CLI command that fires canned prompts at the configured model so structured-output reliability is proven before any agent is built.

**Design notes & edge cases:** Hidden command `dotnet run -- probe`. Cases: (1) plain JSON-only answer, (2) deliberately broken JSON then a repair-feedback turn, (3) short roleplay dialog turn, (4) injection-style string that must be treated as data. Prints a Spectre table: case, latency, parse success. Exit code non-zero if the model fails the JSON contract.

**Acceptance criteria:**
- Probe runs against Jan and exits non-zero when JSON cases fail (CI-style gate for model choice)
- Latency per case printed; results archived to docs as the model acceptance record
- Repair-case proves the model can fix its own invalid JSON given the error

**Subtasks:**
- [x] [AI] Implement raw prompt sender honoring configured endpoint/model/timeout
- [x] [AI] Author the 4 probe cases incl. repair loop and injection-as-data case
- [x] [CS] Render probe results as Spectre table with timings + exit codes

_Depends on: M0-05_

---

### M1 — Core Domain & Deterministic Engine  · _Engine, ~2 weeks_

A headless, fully unit-tested engine: calendar clock, clamped/decaying stats, entity graph, declarative actions (preconditions → transactional effects), explicit turn state machine and append-only journal — proven on a C#-hardcoded demo world. Zero AI involved.

> **Checkbox status corrections (2026-09-21).** Several M1 subtasks overstated completion. One
> consistent mechanism is used below and every marked line cites an item in
> [`correctness-tracker.md`](correctness-tracker.md): **re-opened `[ ]`** — the named deliverable
> does not exist; **partial `[x]`** — the code exists but the acceptance criterion is unmet or
> unproven. No scope, acceptance criterion or story id changed.

**Exit gate:**
- Scripted 7-day simulation runs end-to-end in an integration test
- Failing preconditions provably mutate nothing (snapshot-equality tests)
- Every clock rollover, stat clamp and threshold event covered by unit tests

> **Exit-gate status (2026-09-21): not passed.** The re-opened/partial subtasks in this milestone
> and tracker items D1–D12 / V1–V4 are open; see
> [`correctness-tracker.md`](correctness-tracker.md).

#### M1-01 [CS] Game clock & calendar  · _Must · M_

As the engine, I need one authoritative time source (day of week, hour, day phase) so schedules, decay and deadlines have something to hang off.

**Design notes & edge cases:** GameClock { DayIndex, DayOfWeek, Hour, Minute } advanced only via Advance(minutes) called by the action resolver. Phases (configurable per world): Night 00–05, Morning 06–11, Midday 12–16, Evening 17–21, Night 22–23. Emits HourPassed / DayStarted engine events. Edge cases: multi-hour actions crossing midnight and week boundaries; sleep-until-morning clamp; actions that would cross an opening-hours boundary.

**Acceptance criteria:**
- Unit tests cover midnight rollover, week rollover, multi-hour advances and phase mapping
- Clock cannot be mutated outside the resolver (encapsulation/architecture test)
- DayStarted fires exactly once per day change regardless of advance size

**Subtasks:**
- [x] [CS] GameClock value type + DayPhase enum + Advance API
- [x] [CS] Internal event dispatcher publishing HourPassed/DayStarted — _partial (D12: dispatcher has zero call sites)._
- [x] [CS] Rollover/phase unit-test matrix (dozen+ cases)


#### M1-02 [CS] Stat set, decay & critical thresholds  · _Must · M_

As the engine, I need clamped stats (energy, hunger, mood, hygiene, health, stress, money) with per-hour decay so time pressure exists and content can reason about needs.

**Design notes & edge cases:** StatDef comes from world rules: min/max, decayPerHour, criticalAt, onCritical consequence. Decay applied on HourPassed. Critical crossings raise StatCritical once (re-arm after recovery) → engine consequences, e.g. energy 0 → pass out (forced sleep of N hours, mood penalty); hunger 0 → health drain per hour. Money is decimal; negative allowed only when world rules enable debt. All deltas clamp at bounds — never silently overflow.

**Acceptance criteria:**
- Stats clamp exactly at [min,max] incl. single huge positive/negative deltas
- Threshold event fires once per crossing, not every tick (no event spam)
- Pass-out and starvation consequences covered by unit tests

**Subtasks:**
- [x] [CS] StatSet + StatDef model with decay application on HourPassed
- [x] [CS] Threshold detector with once-per-crossing semantics
- [x] [CS] Consequence hooks (pass-out, starvation) + tests

_Depends on: M1-01_

#### M1-03 [CS] Entity model — player, NPCs, locations, relationships  · _Must · L_

As the engine, I need the complete in-memory world state so actions and AI context building have a single ground truth.

**Design notes & edge cases:** Player: stats, skills (XP curve + prerequisites), traits, inventory (items with effects), money, locationId. Npc: stat subset, mood, weekly schedule (day→time blocks→location), relationship −100..100 with milestone crossings (25/50/75) fired once, flags. Location graph: connections, allowed action ids, optional opening hours. WorldState aggregate owns entity maps; lookup by id everywhere (never by name).

**Acceptance criteria:**
- Schedule resolution returns a location for every (day, hour) with home fallback
- Relationship deltas clamp at ±100 and emit milestone events exactly once
- Location graph supports one-way connections and gated edges (requires key/flag)

**Subtasks:**
- [x] [CS] Player/Npc/Location/Item/Skill entity classes inside WorldState
- [x] [CS] Weekly schedule model + resolver + tests
- [x] [CS] Relationship tracker with milestone events
- [x] [CS] Skill XP/level model with prerequisite checks

_Depends on: M1-02_

#### M1-04 [CS] Action system & resolver  · _Must · L_

As the engine, I need declarative actions (costs, preconditions, effects) validated and applied transactionally, so no UI or AI path can ever corrupt state.

**Design notes & edge cases:** ActionDefinition { id, verb, category, timeCost, energyCost, moneyCost, requirements[], effects[] }. Requirement kinds: StatGte, SkillGte, LocationIs/LocationType, RelationshipGte, HasItem, FlagSet, TimeWindow. Effect kinds: StatDelta, Money, SkillXp, Relationship, SetFlag, Move, Unlock. Resolve: evaluate all requirements → on failure return ActionResult.Failure(reasons) and mutate nothing; on success apply all effects atomically, advance clock, append journal entry, emit events. Built-in verbs: Move, Talk, Work, Study, Eat, Sleep, Shop, Exercise, Socialize, Custom (world-defined).

**Acceptance criteria:**
- Failed precondition → snapshot equality before/after (proven by test)
- Effects batch-apply atomically — no partial application on mid-apply error
- Every requirement and effect kind has happy-path + rejection tests
- Unknown action id / target id produces exhaustive readable errors

**Subtasks:**
- [x] [CS] ActionDefinition + requirement/effect DSL model
- [x] [CS] Validator pipeline producing ordered failure reasons
- [x] [CS] Transactional apply + clock advance + journal write
- [x] [CS] Evaluator tests for every kind (incl. atomicity & clamp interplay) — _partial (V1: atomicity proof missing)._

_Depends on: M1-03_

#### M1-05 [CS] Turn state machine / game loop core  · _Must · M_

As the engine, I need an explicit turn pipeline so UI and AI stages plug into defined seams and failures route to a typed recovery path.

**Design notes & edge cases:** States: Idle → InputReceived → Translating* → Validating → Applying → WorldTick → Narrating* → Options* → Idle. Stages marked * are AI seams: injectable delegates with null (scripted) implementations here in M1 and real agents in M5. Every stage transition journaled with the turn correlation id. Any stage failure → Recovery with typed TurnError; the loop never throws across the UI boundary.

**Acceptance criteria:**
- Legal/illegal transitions tested; Recovery path tested per stage
- One scripted command can be driven end-to-end headless (no UI, no AI)
- Every transition writes a journal entry carrying the correlation id

**Subtasks:**
- [x] [CS] GameLoop with stage delegates + TurnResult/TurnError types
- [x] [CS] Recovery routing (no unhandled exceptions escape the loop) — _partial (V2: no Recovery state)._
- [x] [CS] Headless harness used by integration tests

_Depends on: M1-04_

#### M1-06 [CS] Event journal  · _Must · S_

As the engine, I need an append-only journal so saves, AI context, debugging and replays share one source of truth.

**Design notes & edge cases:** JournalEntry { seq, correlationId, simTime, type, payload }. Query API: last N, by type, since(day). Entries classified pinned (always-in-context facts like world id, player name) vs rolling (evictable). Basis for: LLM context packets (M4-04), save snapshots (M6), replay debugging tool, AI-grounding (AI receives journal facts, not raw prose history).

**Acceptance criteria:**
- Query API covers last-N / by-type / since-day with correct ordinal ordering
- Pinned vs rolling classification respected by the context builder later
- Replay: re-applying the journal from day 0 reproduces the state hash

**Subtasks:**
- [ ] [CS] Journal store (append-only) + typed payload records — _re-opened (D7: payload is a string, not typed records)._
- [x] [CS] Query API + pinned/rolling classification
- [x] [CS] Replay tool used by tests (state-hash comparison) — _partial (D7: replay is a special case, not general)._

_Depends on: M1-05_

#### M1-07 [CS] Hardcoded demo world (dogfood fixture)  · _Must · M_

As a developer, I want a small C#-authored world so the whole engine is exercisable before the markdown loader exists — and which remains a permanent test fixture.

**Design notes & edge cases:** Scope: 3 locations (flat, café, office), 3 NPCs with simple schedules, 6 actions (move/talk/work/eat/sleep/rest), 1 goal. Purpose: decouple M1 from M2; later superseded by markdown worlds but kept in the test assembly as the fastest integration fixture.

**Acceptance criteria:**
- 7-day scripted playthrough integration test passes (work→eat→sleep keeps stats alive)
- Fixture lives in the test project and runs in milliseconds

**Subtasks:**
- [ ] [CS] DemoWorld builder with entities + actions + one goal — _re-opened (D10: no Goal type, no talk action)._
- [x] [CS] Scripted-strategy playthrough integration test

_Depends on: M1-05_

---

### M2 — World Definition Format & Markdown Loader  · _Content pipeline, ~2 weeks_

Author game worlds as folders of markdown files with YAML frontmatter; parse (Markdig), validate (referential integrity) and materialize them into deterministic runtime state. Agent prompt templates ship inside the world.

**Exit gate:**
- Sample world “classic-life” loads with zero errors
- Fixture world with 15 planted faults yields 15+ precise diagnostics and refuses to load
- World format reference page written (every frontmatter field documented)

#### M2-01 [CS] Markdown + frontmatter parsing pipeline  · _Must · M_

As a world author, I want each entity described in one readable markdown file so content stays approachable to non-programmers.

**Design notes & edge cases:** Markdig with YamlFrontMatter extension: split frontmatter → typed record via YamlDotNet (strict mode: unknown keys → warnings), body kept as raw prose sections (## Description, ## Personality, ## GM Notes…) which later feed AI context packets. Diagnostic model: severity, file path, line, message — accumulated, never throw-on-first.

**Acceptance criteria:**
- Malformed YAML produces file:line diagnostics (not crashes)
- Unknown frontmatter keys → warnings; missing required keys → errors
- Body sections parsed and addressable by heading

**Subtasks:**
- [ ] [CS] Parse stage: frontmatter split + strict YAML mapping + body sections
- [ ] [CS] Diagnostic accumulator model shared by all loaders
- [ ] [CS] Golden-file tests: fixture markdown → expected definition


#### M2-02 [CS] World manifest (world.yml) & compatibility gate  · _Must · S_

As a player, I want world packs to declare engine compatibility so loading an incompatible world fails fast with a clear message instead of undefined behavior mid-game.

**Design notes & edge cases:** world.yml: id, name, version (semver), engineVersion (semver range), author, description, tags, defaultPlayer { name, startingLocation, startingMoney, statOverrides }. Engine verifies its own version against the range before loading entities; mismatch → friendly refusal naming required range.

**Acceptance criteria:**
- Incompatible engineVersion → refusal panel naming world requirement vs actual
- Missing manifest → hard error; unknown manifest keys → warnings
- defaultPlayer materializes into the new-game state

**Subtasks:**
- [ ] [CS] Manifest model + semver-range evaluator
- [ ] [CS] Compatibility gate + refusal UI copy
- [ ] [CS] defaultPlayer → new-game materialization

_Depends on: M2-01_

#### M2-03 [CS] Entity loaders — locations, characters, traits, skills, items, actions, arcs  · _Must · L_

As the engine, I want one loader per entity kind so folder conventions map 1:1 onto typed registries.

**Design notes & edge cases:** Conventions: locations/*.md, characters/*.md, traits/*.md, skills/*.md, items/*.md, actions/*.md, arcs/*.md. Character frontmatter: id, name, age, occupation, homeLocation, traits[], schedule (compact DSL `mon-fri 09-17 office`), relationshipStart. Location: connections[], allowedActions[], openHours. Action md: verb, category, costs, requirements, effects in YAML; body = narrator hints for the AI. Skill: levels, xpCurve, prerequisites. Arc: ordered stages with flag triggers. Prompt templates: prompts/*.md with {{placeholders}} (engine validates them).

**Acceptance criteria:**
- Every entity kind round-trips file → definition for the sample world
- Schedule DSL parses `mon-fri 09-17 office` style entries incl. overnight ranges
- Action markdown effects map onto the M1 requirement/effect DSL exactly
- Prompt template placeholders are inventoried for validation

**Subtasks:**
- [ ] [CS] Loader base class + per-kind typed loaders wiring the diagnostic model
- [ ] [CS] Schedule DSL parser + edge-case tests (overnight, missing days)
- [ ] [CS] Action markdown → requirement/effect DSL mapping
- [ ] [CS] Arc stage/trigger model

_Depends on: M2-01_

#### M2-04 [CS] World validator & integrity report  · _Must · L_

As a world author, I want the validator to find every broken reference before runtime so content bugs don't surface mid-game.

**Design notes & edge cases:** Checks: unique ids across all kinds; location connections exist and are intentional (asymmetry → warning); NPC homeLocation/traits/schedule locations exist; every action requirement/effect references a real stat/skill/item/location/flag; arc triggers reference known flags; prompts/*.md placeholders ⊆ engine-provided set; economy sanity warnings (weekly income vs mandatory costs). Aggregated report grouped by file, printed as Spectre table and written next to the world.

**Acceptance criteria:**
- Fixture world with 15 planted faults → 15+ diagnostics and load refusal
- Sample world → zero errors (warnings allowed and listed)
- Report includes file, line when available, and suggested fix

**Subtasks:**
- [ ] [CS] Referential-integrity checkers per relation type
- [ ] [CS] Aggregate reporter (console table + world VALIDATION.txt)
- [ ] [CS] Fault-fixture world used by tests

_Depends on: M2-03_

#### M2-05 [CS] Runtime materialization & world registry  · _Must · M_

As the engine, I want definitions materialized into live entities with defaults applied, so a new game boots purely from markdown.

**Design notes & edge cases:** Definition→Entity factories: apply defaultPlayer, initialize empty NPC memory summaries, arm arc stage triggers, set flags. WorldRegistry provides id-lookup for all kinds plus the RefWhitelist used later by M5-08 to reject AI-invented references. New-game state must be deterministic for same world+config+seed.

**Acceptance criteria:**
- New-game state hash is deterministic across runs for same inputs
- RefWhitelist enumerates every valid entity/flag/location id
- Materialization errors impossible post-validation (proof by construction tests)

**Subtasks:**
- [ ] [CS] Factories + initial WorldState assembly
- [ ] [CS] WorldRegistry + RefWhitelist provider
- [ ] [CS] Determinism test (double-boot hash equality)

_Depends on: M2-04_

#### M2-06 [CS] Sample world “classic-life” (MVP content)  · _Must · XL_

As a player, I want a shipped world so the engine is playable out of the box and every system has real content to chew on.

**Design notes & edge cases:** MVP scope here (full polish in M8-01): 6 locations (apartment, office, café, gym, park, corner store), 6 NPCs with distinct voices/schedules (boss, barista, neighbor, trainer, old friend, shopkeeper), skills (coding, fitness, charisma, cooking), basic economy (salary + weekly rent event), starter goals (career, relationship, wealth tracks), tuned prompt templates. This is human-life-sim: earn money, learn skills, improve relationships, raise a life.

**Acceptance criteria:**
- Validator-clean load of the whole world
- Scripted 14-day survival playthrough passes headless
- Every action category used at least twice; every location reachable by day 1

**Subtasks:**
- [ ] [CS] Author 6 locations with connections + open hours
- [ ] [CS] Author 6 characters with schedules, traits, voices
- [ ] [CS] Author the action set (job/study/economy/social/self-care)
- [ ] [CS] Author rules: needs decay, rent event, goal/win-lose definitions

_Depends on: M2-05_

---

### M3 — Console UI & Player Experience  · _Experience, ~2 weeks_

A polished Spectre.Console interface — title/world-select shell, persistent HUD (clock + stat bars), narrative panel, dialogs, grouped action menu with costs and disabled reasons, free-text input, pause/settings/journal — playable end-to-end against scripted (non-AI) content with all AI seams wired to fallbacks.

**Exit gate:**
- Complete offline playthrough driven only through the UI
- Spectre markup-injection from hostile text provably escaped (test fixtures)
- TestConsole snapshot tests for HUD, menu and dialog screens

#### M3-01 [CS] App shell — title, world select, new/load game, quit  · _Must · M_

As a player, I want a clean entry flow so that starting a life takes seconds: pick world → new game → play.

**Design notes & edge cases:** Stack-based screen router: Title → WorldSelect (reads ./worlds + user library dir, shows validator status badge per world) → NewGame (name prompt, uses defaultPlayer) / LoadGame (M6) → GameLoop → Pause → Quit (autosave prompt, M6 wires it; placeholder before then). Graceful handling of empty library with a helpful setup hint.

**Acceptance criteria:**
- Navigation stack supports back/quit from every screen without dead ends
- World list shows load-health per world (OK / warnings / refused with reason)
- Empty library state renders actionable instructions

**Subtasks:**
- [ ] [CS] Screen router + frame redraw loop
- [ ] [CS] Title + WorldSelect screens with validator badges
- [ ] [CS] NewGame flow (name prompt → materialized state)

_Depends on: M2-05_

#### M3-02 [CS] HUD status panel  · _Must · M_

As a player, I want my clock, stats and money always visible so I can plan my day without opening menus.

**Design notes & edge cases:** Persistent top panel: `Mon · Day 3 · 14:20 · Evening`, stat bars colored by threshold (≥50 accent, 20–49 amber, <20 red), money, current location. Breakdown rule separating HUD from narrative. Renders via a pure function of WorldState → layout, enabling TestConsole snapshot tests at 80/100/120 column widths.

**Acceptance criteria:**
- Snapshot tests at 3 terminal widths, incl. too-narrow degraded layout
- Threshold colors switch exactly at configured boundaries
- HUD re-renders only on state change (no flicker storm)

**Subtasks:**
- [ ] [CS] Pure HUD render function + Spectre layout (columns/bars)
- [ ] [CS] Threshold color mapping from StatDef.criticalAt
- [ ] [CS] Snapshot tests via TestConsole

_Depends on: M3-01_

#### M3-03 [CS] Narrative panel & dialog rendering  · _Must · M_

As a player, I want readable prose and clearly attributed dialogs so the world feels alive in a terminal.

**Design notes & edge cases:** Prose panel with comfortable measure (wrap ≤ 76 chars), paragraph spacing. Dialog: per-NPC accent color + name prefix, subtle indent. CRITICAL EDGE CASE: all external text (AI prose, world markdown, NPC names) passes an escaping pipeline before Spectre render — raw `[`, `]`, control chars would otherwise crash markup parsing. Fixture tests fire hostile strings at the renderer.

**Acceptance criteria:**
- Hostile fixtures (`[/]`, `[bold`, control chars) render literally, no crash
- Long paragraphs wrap at the configured measure with hanging indent
- Per-NPC dialog colors assigned deterministically from NPC id

**Subtasks:**
- [ ] [CS] Escaping pipeline (single choke point for all external text)
- [ ] [CS] Prose panel + dialog renderer with NPC palette
- [ ] [CS] Hostile-string fixture tests

_Depends on: M3-01_

#### M3-04 [CS] Action menu with costs & disabled reasons  · _Must · M_

As a player, I want my options grouped with visible costs and honest disabled reasons so choices feel informative, not trial-and-error.

**Design notes & edge cases:** SelectionPrompt grouped by category (Work / Social / Self-care / Move), each entry: label + compact cost suffix (`2h · 15⚡` rendered as text glyphs, no emoji), disabled entries greyed with reason (`too tired`, `closed until 09:00`, `needs Cooking 2`). Keyboard navigation; final entry is always `✎ free text…` which routes to M3-05. Source: option list injected by the loop (rule-based now, AI-augmented in M5).

**Acceptance criteria:**
- Precondition failures appear as disabled entries with human reasons
- Costs shown match exactly what the resolver will charge
- Menu handles 0 available actions (forces free text / rest fallback)

**Subtasks:**
- [ ] [CS] Option→menu-item mapper with cost suffixes + disabled styling
- [ ] [CS] Grouped SelectionPrompt with free-text escape hatch
- [ ] [CS] Empty-options edge handling

_Depends on: M3-02_

#### M3-05 [CS] Free-text input mode  · _Must · S_

As a player, I want to type natural commands (“nap until evening”, “ask Mia about the job”) so the game feels like a simulation, not a menu tree.

**Design notes & edge cases:** TextPrompt with history (↑ recall, in-memory), Esc/empty cancels back to menu, inline hint that AI is OFFLINE and free text is unavailable when Ai:Enabled=false or Jan unreachable (menu still fully playable). Input goes to the Translation seam (scripted echo until M5-03).

**Acceptance criteria:**
- History recall works for the session; cancel returns to action menu
- AI-offline state shows the hint and blocks submission politely
- 2000-char paste does not break layout (truncated with notice)

**Subtasks:**
- [ ] [CS] Free-text prompt with history + cancel semantics
- [ ] [CS] AI-availability hint wiring
- [ ] [CS] Oversized input guard

_Depends on: M3-04_

#### M3-06 [CS] Pause menu, settings & journal viewer  · _Should · M_

As a player, I want to tweak settings and inspect my journey without leaving the game.

**Design notes & edge cases:** Pause: Resume / Journal / Settings / Save (M6) / Quit-to-title. Settings: AI on/off, model name, verbosity, theme, reset-to-defaults — persisted to user config. Journal viewer: scrollable day-grouped event list with type filter (All / Actions / Dialogs / Events / Milestones).

**Acceptance criteria:**
- Settings persist to ~/.lifesim/config.json and apply without restart
- Journal filter round-trip: 2000 events render without input lag
- Theme change restyles existing screens on next frame

**Subtasks:**
- [ ] [CS] Pause menu wiring + quit-to-title state reset
- [ ] [CS] Settings screen writing user config
- [ ] [CS] Journal viewer with day grouping + filters

_Depends on: M3-02_

#### M3-07 [CS] Event notifications  · _Should · S_

As a player, I want level-ups, critical needs and relationship milestones surfaced as brief toasts so I don't miss turning points.

**Design notes & edge cases:** Subscribes to engine events (SkillLevelUp, StatCritical, RelationshipMilestone, MoneyChanged, ArcStageCompleted). Toast queue: one at a time, ~2.5s dwell, compact one-liners; quiet mode in settings collapses them into the journal only.

**Acceptance criteria:**
- Events fired mid-frame queue instead of overlapping
- Quiet mode suppresses toasts but keeps journal entries
- Burst of 10 events drains in order without dropping any

**Subtasks:**
- [ ] [CS] Toast queue + renderer
- [ ] [CS] Event→toast mapping for the five event types
- [ ] [CS] Quiet-mode setting

_Depends on: M3-02_

#### M3-08 [CS] Text pipeline & rule-based fallback rendering  · _Must · M_

As the engine, I want one rendering path for AI prose and scripted prose so offline mode is a first-class experience, not a broken one.

**Design notes & edge cases:** ITextSurface abstraction: paragraphs(), dialog(npc, line), outcome(diff). The fallback outcome formatter renders ActionResult diffs compactly (“Stocked shelves at the café. +120 money −35 energy −2h”). Later, AI prose flows through the identical surface — guaranteeing the game is fully playable with the LLM off and making every UI test deterministic.

**Acceptance criteria:**
- AI prose and fallback prose share one code path (verified by test double)
- Fallback outcome text mentions every applied effect (nothing silent)
- Offline full playthrough requires zero AI calls (asserted via fake client counting calls)

**Subtasks:**
- [ ] [CS] ITextSurface + Spectre implementation
- [ ] [CS] Fallback outcome formatter from ActionResult diffs
- [ ] [CS] Offline playthrough test asserting zero LLM calls

_Depends on: M3-03_

---

### M4 — LLM Client & AI Infrastructure (Jan)  · _AI infrastructure, ~1.5 weeks_

Everything agents need, none of the agents yet: resilient OpenAI-compatible client over Microsoft.Extensions.AI, retry/timeout/circuit-breaker, strict structured-output with schema validation and repair loop, context builder with memory & token budget, markdown prompt-template engine, call cache and replay fixtures.

**Exit gate:**
- Structured-output suite green against a fake IChatClient (incl. rejection paths)
- Killing Jan mid-session degrades gracefully — no crash, clear banner, game continues
- Live smoke checklist passes against the real Jan endpoint

#### M4-01 [AI] Jan LLM client (OpenAI-compatible, via Microsoft.Extensions.AI)  · _Must · M_

As the engine, I want a thin, testable client over IChatClient so the model provider stays swappable and unit tests never touch the network.

**Design notes & edge cases:** Microsoft.Extensions.AI OpenAI adapter pointed at config endpoint (default http://127.0.0.1:1337/v1, api-key dummy as Jan requires none). Model from config; ChatOptions per call (temperature, maxTokens, stop). Startup health check: GET /v1/models with 2s timeout → AiStatus { Online, Degraded(slow), Offline } shown in HUD banner. Streaming evaluated for narrative latency perception: default OFF for simplicity, config flag enables for Narrator only.

**Acceptance criteria:**
- Fake IChatClient drives all unit tests (zero network)
- Health check classifies reachable-but-slow as Degraded
- Endpoint/model changes take effect from config without recompile

**Subtasks:**
- [ ] [AI] IChatClient wiring + per-call ChatOptions from agent config
- [ ] [CS] Startup health check + AiStatus HUD banner
- [ ] [AI] Streaming spike behind config flag (Narrator only), verdict recorded in ADR

_Depends on: M0-06_

#### M4-02 [AI] Resilience — retry, timeout, circuit breaker  · _Must · M_

As a player, I want hiccups in the local model to be invisible-or-harmless so my game never crashes because an LLM sneezed.

**Design notes & edge cases:** Polly v8 pipeline per call site: 2 retries with jittered backoff (300ms/1200ms), per-call timeout from config (default 45s), circuit breaker: 3 consecutive failures → open 60s → half-open probe. All exits funnel into typed LlmError { kind: Timeout|Refused|Malformed|CircuitOpen } consumed by the orchestrator's fallback routing. Cancellation token plumbed from UI (Esc aborts in-flight call).

**Acceptance criteria:**
- Fault-injection tests: timeout, refuse, hang — each ends in typed LlmError, never exception across the seam
- Circuit opens after 3 failures and half-open probe recovers
- Esc cancels an in-flight call and returns control to the menu

**Subtasks:**
- [ ] [AI] Polly pipeline builder + typed LlmError taxonomy
- [ ] [CS] Cancellation plumbing from UI down to the client
- [ ] [AI] Fault-injection test suite against fake transport

_Depends on: M4-01_

#### M4-03 [AI] Structured output & repair loop  · _Must · L_

As the engine, I want machine-checked JSON from the model so downstream stages consume contracts, not vibes.

**Design notes & edge cases:** Pattern per agent: system prompt declares OUTPUT CONTRACT (schema + 2 few-shot examples + 'JSON only, no prose'). Parse with System.Text.Json (strict); validate with JsonSchema.Net plus semantic validation against the world RefWhitelist. REPAIR LOOP: on invalid output, append the error (`your previous output was invalid: <parser error>`) and retry — max 2 repairs → typed failure → stage fallback. Every attempt journaled to llm-calls.jsonl.

**Acceptance criteria:**
- Fixture tests: valid, trailing-prose, truncated, wrong-type, hallucinated-ref — each classified correctly
- Repair succeeds ≤2 attempts or yields typed failure
- Zero regex scraping: single strict parse path (plus an extract-JSON fence trimmer as the only leniency)

**Subtasks:**
- [ ] [AI] Schema-per-agent contract definitions + few-shot examples
- [ ] [AI] Strict parse + JsonSchema + RefWhitelist semantic validation
- [ ] [AI] Repair loop with error feedback (max 2, journaled)

_Depends on: M4-01_

#### M4-04 [AI] Context builder & memory  · _Must · L_

As an agent, I want a compact, truthful context packet so I stay grounded in the actual world instead of hallucinating.

**Design notes & edge cases:** ContextPacket composition per agent: PINNED (clock, location facts, local NPC sheets, player stats, active goals, arc state), ROLLING (last N journal events + last K dialog turns), SUMMARIZED (per-NPC running memory summary — updated after each scene by cheap LLM pass or extractive fallback). Token budgeter: chars/4 heuristic with configurable headroom; overflow policy: evict oldest rolling → re-summarize → truncate with marker. Budgets per agent (narrator ~1200 tok, dialog ~1500, translator ~800, options ~600, director ~1000 — tuned in M9).

**Acceptance criteria:**
- Packet builder provably respects budget under a 10k-event journal
- NPC memory summary updates after a dialog and survives save/load
- Pinned facts never evicted (property test)

**Subtasks:**
- [ ] [AI] ContextPacket model + per-agent composers
- [ ] [AI] Token budgeter + eviction policy
- [ ] [AI] NPC memory summarizer (+ extractive offline fallback)

_Depends on: M4-01_

#### M4-05 [AI] Prompt template engine  · _Must · S_

As a world author, I want agent prompts to be markdown files I can override per world so tone belongs to the content, not the binary.

**Design notes & edge cases:** Templates = markdown with {{Placeholder}} and {{#Section}} blocks (engine interpolates strictly — missing placeholder data → world-load error, caught by validator). Resolution order: world prompts/narrator.md → embedded engine default. Template unit tests snapshot-render with fixed packets. Placeholder inventory per agent documented in authoring docs.

**Acceptance criteria:**
- World override replaces engine default (precedence test)
- Unknown placeholder in world template → validator error at load time
- Rendered snapshots stable under fixed input (unit tests)

**Subtasks:**
- [ ] [AI] Tiny mustache-style interpolator (strict, no logic beyond sections)
- [ ] [CS] Engine-embedded default templates per agent
- [ ] [CS] Validator integration for placeholder inventory

_Depends on: M2-03_

#### M4-06 [AI] Call cache & replay fixtures  · _Should · M_

As a developer, I want deterministic replay of recorded model responses so iteration is fast and the test suite runs with no model at all.

**Design notes & edge cases:** Dev-time disk cache keyed by hash(system+user+model+params) under .lifesim/cache (gitignored, size-capped; disabled by config in production runs). Fixture compiler converts llm-calls.jsonl journals into test fixtures (request → response pairs) consumed by the fake IChatClient. Foundation for the M9-02 prompt-regression suite.

**Acceptance criteria:**
- Cache hit replays byte-identical response without a live endpoint
- Fixture compiler emits runnable C# fixture from a recorded journal
- Cache disabled mode passes the exact-input-passthrough property test

**Subtasks:**
- [ ] [AI] Content-hash cache with size cap + config toggle
- [ ] [CS] Journal→fixture compiler
- [ ] [CS] Fake IChatClient fixture playback

_Depends on: M0-04_

---

### M5 — Agent Layer & Turn Pipeline  · _AI, ~2.5 weeks_

The five agents live and orchestrated per turn: Translator → engine validation → apply → Director world tick → Narrator → Options — each with independent timeouts, validation and rule-based fallbacks. Player experience: type anything, get a legal action; always get grounded options.

**Exit gate:**
- Full turn playable with the live model: free text, dialog, options, narration
- Chaos suite passes: mock failure injected at every single stage keeps state consistent
- AI-invented entities/props rejected by whitelist validation in 100% of seeded cases

#### M5-01 [AI] Narrator agent  · _Must · M_

As a player, I want outcomes narrated in vivid second-person prose grounded in the place and time so the world feels authored.

**Design notes & edge cases:** Input: action result diff (what mechanically changed), location markdown, clock/phase, world tone guide, last 2 narrative beats. Output: 1–2 paragraphs present tense, no stats jargon, no invented facts (validated cheaply: no unknown proper nouns vs whitelist, warning-only). Temp 0.7. Timeout 20s → fallback: compact rule-based outcome text (M3-08). Never blocks progression.

**Acceptance criteria:**
- Narration mentions the actual location name and time phase (grounding check)
- Timeout/malformed → fallback text with zero player-facing breakage
- Stats are never restated as raw numbers in prose (lint check)

**Subtasks:**
- [ ] [AI] prompts/narrator.md default template + contract
- [ ] [AI] Agent implementation over M4 infrastructure
- [ ] [CS] Prose vs whitelist name-lint + fallback wiring

_Depends on: M4-03, M4-04, M4-05_

#### M5-02 [AI] Options agent  · _Must · L_

As a player, I want fresh, context-aware action options each turn so the game suggests more than my habits.

**Design notes & edge cases:** Input: location + connected locations, player stats/skills/inventory, clock, NPCs present (via schedules), active goals, recent actions (anti-repeat list). Output JSON: 3–6 items { label, actionType, targetRef, params?, hint? }. Engine-side: validate actionType ∈ catalog at this location; targetRef ∈ whitelist; preconditions evaluate; drop/label-disabled invalid items; dedupe by (verb,target). Fallback: rule-based generator (M5-07). Timeout 30s.

**Acceptance criteria:**
- 100% of emitted refs exist (whitelist assertion in fixtures + live runs)
- Invalid/duplicate options are filtered, not rendered
- Anti-repeat: same menu twice in a row only if generator truly exhausted

**Subtasks:**
- [ ] [AI] prompts/options.md + output contract + few-shots
- [ ] [CS] Option validation/filter pipeline (whitelist + preconditions + dedupe)
- [ ] [AI] Agent implementation + timeout/fallback routing

_Depends on: M4-03, M4-04_

#### M5-03 [AI] Action translator agent  · _Must · L_

As a player, I want my free text turned into a legal game action so anything I type has a fair chance of working.

**Design notes & edge cases:** Input: player text + AVAILABLE ACTIONS CATALOG for this location (id, verb, targets, costs) + pinned context. Output JSON: { action, targetRef?, params?, confidence }. Engine validates schema → refs → preconditions. Confidence < 0.6 → ONE clarification question (“Did you mean…?” with 2 candidates) → still ambiguous → drop to action menu. Catalog-constrained prompting is the primary anti-hallucination defense. Timeout 25s.

**Acceptance criteria:**
- Catalog-constrained output: unknown actions structurally impossible in fixtures
- Low-confidence path asks exactly one clarification, then menus
- Injection text inside player input never alters the instruction channel (red-team fixture)

**Subtasks:**
- [ ] [AI] prompts/translator.md + catalog injection format
- [ ] [CS] Confidence gate → clarification → menu cascade
- [ ] [AI] Red-team injection fixtures (instruction-in-player-text)

_Depends on: M4-03, M5-02_

#### M5-04 [AI] NPC agent (dialog & reactions)  · _Must · L_

As a player, I want NPCs with consistent voices and memories so relationships feel earned, not scripted.

**Design notes & edge cases:** One dialog turn at a time (multi-NPC scenes serialize). Input: NPC markdown (personality, speech patterns, boundaries), relationship level + tier label (Stranger…Soulmate), per-NPC memory summary, situation (where/when/why), player utterance. Output: { line, mood, intentTag, suggestedRelDelta }. Engine clamps relDelta to ±5/turn, maps mood → portrait color/NPC mood stat, updates memory summary post-scene. Temp 0.8. Fallback: canned reaction lines tied to mood (“Not now, okay?”).

**Acceptance criteria:**
- Voice consistency check: fixture conversations keep speech-pattern markers
- RelDelta clamped at ±5 regardless of model output (engine-side proof)
- Memory summary reflects the scene (ask-about-past-conversation fixture)

**Subtasks:**
- [ ] [AI] prompts/npc.md + I/O contract + tier labels
- [ ] [CS] RelDelta clamp + mood mapping + memory update hook
- [ ] [AI] Multi-NPC scene serialization policy

_Depends on: M4-04_

#### M5-05 [AI] Director agent (world tick & autonomy)  · _Should · L_

As the world, I want life to continue while the player acts — NPCs move on schedules, small events fire, arcs advance — so time feels real.

**Design notes & edge cases:** Deterministic base: NPC location from schedule (no AI needed). Director ADDs autonomy on top: after elapsed time ≥ threshold or DayStarted, it receives (elapsed summary, NPC states, arc states, rules) and proposes events: { type: NpcIntent|Ambient|ArcBeat, ref, params, narrativeHint } — e.g. neighbor invites you to lunch, rain rolls in, arc stage 2 triggers. Engine validates refs, budget-caps events per tick (≤3), clamps all numeric effects to rule-defined envelopes. Runs at most once per turn; skipped silently if AI offline.

**Acceptance criteria:**
- Zero Director events mutate state outside validated envelopes
- AI offline → schedules still tick (world never freezes)
- Budget cap enforced: ≤3 events per tick regardless of model output

**Subtasks:**
- [ ] [AI] prompts/director.md + event schema + few-shots
- [ ] [CS] Event envelope validation + budget cap
- [ ] [AI] Tick cadence policy (thresholds, once-per-turn)

_Depends on: M4-03, M4-04_

#### M5-06 [AI] Agent orchestrator & pipeline wiring  · _Must · L_

As the engine, I want one orchestrator running the per-turn agent pipeline with per-stage timeouts, fallbacks and telemetry so AI is composable and debuggable.

**Design notes & edge cases:** Wires M1-05 stage delegates: Translate → Validate(engine) → Apply(engine) → WorldTick(+Director) → Narrate → Options. Per-stage contracts, timeouts, fallback delegates, AI kill-switches (debug flags disable a single agent). Every stage's I/O journaled under the turn correlation id. Failure of ANY agent stage NEVER fails the turn — it selects that stage's fallback.

**Acceptance criteria:**
- Chaos test kills each stage in isolation → turn completes via fallback every time
- Telemetry: per-stage latency table available in debug overlay
- Kill-switch flags disable single agents without recompiling

**Subtasks:**
- [ ] [AI] Orchestrator + stage adapters + fallback routing
- [ ] [CS] Debug overlay: stage timings + statuses
- [ ] [CS] Chaos fixtures over the full pipeline

_Depends on: M5-01, M5-02, M5-03_

#### M5-07 [CS] Rule-based fallback generators (offline brain)  · _Must · M_

As a player, I want the game to remain smart offline, so an LLM outage costs flavor, not function.

**Design notes & edge cases:** Deterministic generators used by fallbacks AND offline mode: option generator (legal actions at location, ranked by goal relevance + novelty), outcome formatter (M3-08), NPC canned dialog (mood-keyed lines from character markdown — authors write 5+ per NPC), Director-silent tick. Quality bar: offline mode must be a complete, winnable game.

**Acceptance criteria:**
- Offline playthrough of classic-life reaches a goal end-state
- Offline options never include an action that fails validation
- Canned dialog coverage: every NPC has ≥5 mood-keyed lines

**Subtasks:**
- [ ] [CS] Legal-option enumerator + goal-relevance ranker
- [ ] [CS] Mood-keyed canned dialog authoring + selection
- [ ] [CS] Offline-mode end-to-end playthrough test

_Depends on: M3-08_

#### M5-08 [CS] AI output validation & safety gates (engine side)  · _Must · M_

As the engine, I want a hard validation boundary on all AI output so the model can propose, but only I dispose.

**Design notes & edge cases:** Central AIGate: (1) RefWhitelist — unknown entity/location/item/flag ids rejected; (2) delta clamping — only rule-defined envelopes (rel ±5/turn; money/stat changes only via action effects, never direct); (3) forbidden transitions — actions whose preconditions fail can't be smuggled in via narration params; (4) injection guard — NPC/player/world-authored text delimited as data, instruction hierarchy fixed, world prompt overrides sandboxed (placeholder-only, no system-channel override); (5) all rejections journaled with reason for prompt tuning.

**Acceptance criteria:**
- Seeded hallucination suite: 100% of invented refs rejected + journaled
- Attempted direct money/stat mutation from AI output is structurally impossible
- World prompt override cannot touch the system channel (test)

**Subtasks:**
- [ ] [CS] AIGate component + RefWhitelist enforcement
- [ ] [CS] Delta-envelope clamping rules
- [ ] [CS] Rejection journal + inspection command

_Depends on: M2-05, M4-03_

---

### M6 — Persistence — Save / Load  · _Engine, ~1 week_

Versioned JSON snapshots with atomic writes, checksums, autosave at day start, slot UI with metadata, and compatibility gates between save format / engine / world package versions.

**Exit gate:**
- Roundtrip test: save → load yields identical state hash
- Corrupted save detected via checksum; last-good backup restored
- Loading a save whose world package is missing/incompatible fails with actionable message

#### M6-01 [CS] Save snapshot model  · _Must · M_

As a player, I want my entire run captured in one versioned snapshot so saving is total and debugging is diffable.

**Design notes & edge cases:** SaveGame { formatVersion, engineVersion, worldId, worldVersion, clock, player, npcs (incl. memory summaries), arcs, flags, rngSeed, journalTail (last ~2000 events) }. System.Text.Json, indented — human-readable and diff-friendly. formatVersion is the migration key; v1 ships with 1.0.

**Acceptance criteria:**
- Roundtrip: save→load→save yields byte-equal files (normalized ordering)
- Snapshot validates against a JSON schema before load
- journalTail bounds snapshot size (<1MB for typical runs)

**Subtasks:**
- [ ] [CS] SaveGame record graph + serialization settings (ordered keys, indent)
- [ ] [CS] JSON schema + pre-load validation
- [ ] [CS] Roundtrip byte-equality test

_Depends on: M1-06_

#### M6-02 [CS] Save store & atomic IO  · _Must · M_

As a player, I want saves that cannot be corrupted by a crash so I never lose a life to a power cut.

**Design notes & edge cases:** Slot dir per world under Saves path. Write protocol: write to *.tmp → fsync → atomic rename; checksum footer (SHA-256) verified on load; keep last-good backup per slot. Autosave: at every DayStarted + on quit (separate rotating slots so manual saves aren't clobbered). Disk-full / permission errors → typed error, never partial writes.

**Acceptance criteria:**
- Kill-process-mid-write test: previous save remains intact
- Corrupted file detected via checksum; backup auto-restored with notice
- Autosave rotation: manual slots untouched by autosaves

**Subtasks:**
- [ ] [CS] Atomic write protocol (tmp→fsync→rename) + checksum footer
- [ ] [CS] Slot management + autosave hooks on DayStarted/quit
- [ ] [CS] Corruption + kill-mid-write tests

_Depends on: M6-01_

#### M6-03 [CS] Load with compatibility gates  · _Must · S_

As a player, I want clear answers when a save no longer matches my engine or world version — not a cryptic crash.

**Design notes & edge cases:** Gates in order: formatVersion (migration table; v1 only in 1.0) → engineVersion range → worldId presence in library → worldVersion compatibility (same major). Each failure renders a named, actionable message (e.g. “This save needs world classic-life 1.x — install it to continue”). Prompt templates missing from world → engine defaults silently.

**Acceptance criteria:**
- Each gate failure has a distinct, tested, actionable message
- World upgraded within same major → loads with migration note
- Missing world → load refused, save untouched

**Subtasks:**
- [ ] [CS] Gate chain + typed load-refusal reasons
- [ ] [CS] Migration-table scaffold (v1 identity)
- [ ] [CS] Gate-message UI copy + tests

_Depends on: M6-02, M2-02_

#### M6-04 [CS] Save/load UI  · _Should · S_

As a player, I want slot cards with metadata so I can pick the right life at a glance.

**Design notes & edge cases:** Slot list: world name, day/time, location, money, playtime, timestamp, autosave badge. Actions: load, overwrite-confirm for manual save, delete with confirm. Reads slot headers without full deserialize.

**Acceptance criteria:**
- Header scan of 20 slots renders without perceivable delay
- Overwrite and delete both require explicit confirmation
- Corrupt slot shows as such (not invisible)

**Subtasks:**
- [ ] [CS] Slot-header scanner (partial parse)
- [ ] [CS] Save/load screens + confirmations

_Depends on: M6-02, M3-06_

---

### M7 — World Packages (.lifeworld)  · _Content pipeline, ~1 week_

NuGet-inspired packaging: a .lifeworld zip (manifest + content hashes), a local library folder, install/uninstall/list from zip or folder with integrity + engine-compatibility gates, and `world init|validate|pack` author tooling. Remote feeds explicitly out of scope for 1.0.

**Exit gate:**
- classic-life distributed as .lifeworld installs, validates and runs
- Tampered package rejected by hash verification with clear report
- Two library roots (./worlds + user dir) merged in world-select UI

#### M7-01 [CS] .lifeworld package format  · _Must · M_

As a world author, I want a single-file artifact so sharing a world means sending one file.

**Design notes & edge cases:** .lifeworld = ZIP archive: root world.yml (package id = manifest id@version), content folders as in the authoring layout, plus content.sha256 (file→hash manifest enabling tamper detection). Engine tooling reads/writes deterministically (normalized zip entry order + timestamps) so identical content yields identical hashes.

**Acceptance criteria:**
- Deterministic pack: same input → same zip hash
- Loader reads worlds from folder OR zip transparently
- content.sha256 round-trips on pack/unpack

**Subtasks:**
- [ ] [CS] Deterministic zip pack/unpack helpers
- [ ] [CS] content.sha256 generation + verification
- [ ] [CS] Transparent folder-or-zip mounting in the world loader

_Depends on: M2-04_

#### M7-02 [CS] Library, install & uninstall  · _Must · M_

As a player, I want a local world library with install/uninstall so collecting worlds feels like installing mods without the mess.

**Design notes & edge cases:** Two library roots merged in WorldSelect: ./worlds (portable) + user dir (~/.lifeworlds). Install sources: .lifeworld zip or a plain folder — copied into the user library, hash-verified, validated, then registered. Uninstall: removes files; refuses (with override flag) when existing saves reference the world, printing the affected saves. `world list` renders id/name/version/author/health as a table.

**Acceptance criteria:**
- Install from zip and from folder both land working worlds
- Uninstall blocked by referencing saves unless --force (saves listed)
- Merged view across both roots with duplicate-id warnings

**Subtasks:**
- [ ] [CS] Install pipeline: copy → verify → validate → register
- [ ] [CS] Uninstall with save-reference guard
- [ ] [CS] Merged library query + `world list` UI

_Depends on: M7-01, M6-03_

#### M7-03 [CS] Install-time integrity & compatibility gates  · _Must · S_

As a player, I want broken or incompatible packs to fail loudly at install, never mid-game.

**Design notes & edge cases:** Gate chain: hash verification → manifest compatibility (engineVersion range) → full world validation → prompt-override sandbox check. Single report panel shows every failure grouped by gate; install is all-or-nothing (no half-registered worlds).

**Acceptance criteria:**
- Tampered zip → install refused with hash mismatch detail
- Engine-incompatible manifest → refusal naming required range
- Zero partial installs: every failure leaves the library unchanged

**Subtasks:**
- [ ] [CS] Gate chain wired into install pipeline
- [ ] [CS] Grouped install report panel
- [ ] [CS] All-or-nothing rollback on any gate failure

_Depends on: M7-01_

#### M7-04 [CS] Author tooling — world init / validate / pack  · _Could · M_

As a world author, I want CLI commands that scaffold, lint and package my world so authoring is a tight loop.

**Design notes & edge cases:** Console verbs: `world init <id>` (comment-rich template: one of each entity kind), `world validate <path>` (exact validator output, exit codes for scripting), `world pack <path> [-o out.lifeworld]` (deterministic zip). Documented with the 30-minute tutorial in M8-04.

**Acceptance criteria:**
- init → validate → pack → install roundtrip works on the scaffold
- validate exit code ≠ 0 on errors (CI/author scripting friendly)
- Template passes validator with zero errors out of the box

**Subtasks:**
- [ ] [CS] Command routing + three verbs
- [ ] [CS] Template scaffold content (comment-rich)
- [ ] [CS] Roundtrip integration test

_Depends on: M7-02_

---

### M8 — Sample Worlds, Balance & Authoring Docs  · _Content & balance, ~2 weeks_

Prove the engine generalizes: finish and balance “classic-life”, author a second world with a different premise/tone under a zero-engine-changes rule, and write the authoring docs + AI grounding guidelines.

**Exit gate:**
- Headless balance simulator: 100 simulated months, survival rate & economy in target band
- Second world “fresh-start” plays end-to-end with zero engine code changes
- “Build a world in 30 minutes” tutorial validated by a cold read-through

#### M8-01 [CS] “classic-life” full content & balance pass  · _Must · XL_

As a player, I want a living economy and pacing so weeks feel meaningful and goals reachable with effort.

**Design notes & edge cases:** Deepen the MVP world: 8 NPCs, 3 story arcs (career promotion, rekindled friendship, marathon), economy tuning (income/expenses/rent over a 30-day horizon), skill progression curves (hours-to-level targets), weekly rent + weekend events, explicit win goals per track and fail states (broke, burnout). Balanced via the headless simulator: scripted strategies (balanced, workaholic, social-butterfly) run 100 simulated months each → survival and progression metrics must land in target bands.

**Acceptance criteria:**
- Simulator: balanced strategy survives ≥95% of months; workaholic burns out ≈ intended rate
- Each win goal reachable in ≤60 in-game days by its matching strategy
- All content validator-clean; every arc completable headless

**Subtasks:**
- [ ] [CS] Headless balance simulator with strategy bots + metrics table
- [ ] [CS] Economy + progression tuning loop against metrics
- [ ] [CS] Arc authoring (3 arcs) + win/fail state definitions

_Depends on: M2-06, M6-02_

#### M8-02 [CS] Second world “fresh-start” — the generality proof  · _Must · L_

As the engine, I want a world with a different premise and tone built under a ZERO-engine-changes rule, proving markdown content alone differentiates games.

**Design notes & edge cases:** Premise: slow-life artist colony — wealth is not the goal; craft skills, inspiration mood, exhibitions and relationships are. Reuses: time, energy/needs, money (supplies), relationships, arcs — re-skinned entirely through markdown (stats renamed via rules, new actions/skills/NPCs/prompts with a warmer tone). The rule: any feature needed that markdown can't express goes on the post-1.0 backlog instead of special-casing the engine.

**Acceptance criteria:**
- Plays end-to-end with zero engine code changes (git-verified)
- Distinct tone: prompt overrides + content produce visibly different narration
- Validation-clean pack installable as .lifeworld

**Subtasks:**
- [ ] [CS] Author world bible, locations, 6 NPCs, skills/actions
- [ ] [AI] Author tone guide + prompt overrides for this world
- [ ] [CS] Pack + install + headless playthrough test

_Depends on: M7-02, M8-01_

#### M8-03 [AI] World-authoring AI guidelines  · _Should · S_

As a world author, I want guidelines for writing character sheets and prompt overrides that ground the model, so my NPCs roleplay consistently.

**Design notes & edge cases:** docs/ai-authoring.md: anatomy of a good character sheet (voice samples, quirks, boundaries, 3 example lines per mood), how schedules/traits steer the Director, prompt-override conventions (placeholder inventory per agent), do/don't with real transcripts (good grounding vs hallucinated lore), and a troubleshooting matrix (symptom → cause → fix).

**Acceptance criteria:**
- Includes ≥2 annotated transcripts contrasting grounded vs hallucinated output
- Placeholder inventory table per agent is complete (validated against engine)
- Troubleshooting matrix covers the top 6 observed failure modes

**Subtasks:**
- [ ] [AI] Write guidelines + transcripts + matrix
- [ ] [CS] Doc-test: placeholder tables generated from engine constants

_Depends on: M5-06_

#### M8-04 [CS] Authoring docs & 30-minute tutorial  · _Should · M_

As a world author, I want a format reference and a hands-on tutorial so I can ship my own .lifeworld in an evening.

**Design notes & edge cases:** docs/world-format.md: every folder kind, every frontmatter field (type, required, default, examples) — generated where possible from engine schema to prevent doc rot. docs/tutorial-your-first-world.md: build a 2-location, 1-NPC micro world in 30 minutes using `world init`, closing with pack+install+play.

**Acceptance criteria:**
- Field reference covers 100% of validator-checked keys (doc-completeness test)
- Tutorial verified by cold read-through with timer (<30 min achievable)
- Both docs linked from README and `world init` output

**Subtasks:**
- [ ] [CS] Format reference (schema-generated snippets + hand-written examples)
- [ ] [CS] Tutorial with checkpoints + timing
- [ ] [CS] Doc-completeness test vs validator key inventory

_Depends on: M7-04_

---

### M9 — QA, Hardening & 1.0  · _Hardening, ~1.5 weeks_

Coverage push on the engine core, prompt-regression fixtures replayed offline, soak + chaos sessions, latency UX polish, and the 1.0 finish: splash, themes, README, changelog, tag.

**Exit gate:**
- Engine core line coverage ≥ 80%; invariant-checked fuzz runs never corrupt state
- 500-turn soak stable (flat RSS, no leaked handles) with LLM killed at random points
- Cold-start < 2s; README lets a new user reach gameplay in < 30 minutes

#### M9-01 [CS] Coverage & golden-path integration  · _Must · L_

As a maintainer, I want a safety net broad enough that refactors and content changes can't silently break the engine.

**Design notes & edge cases:** Push engine core to ≥80% line coverage (clock, stats, resolver, validator, materialization, saves). Golden-path integration: scripted 7-day playthrough per shipped world via the headless harness. Fuzz: random legal-action sequences with invariant checks after every turn (stats in bounds, refs valid, journal monotone) — 10k turns seeded and recorded.

**Acceptance criteria:**
- Coverage gate ≥80% on LifeSim.Core enforced by script
- Golden-path playthroughs green for both shipped worlds
- Seeded fuzz failures replay deterministically (seed recorded in test name)

**Subtasks:**
- [ ] [CS] Coverage measurement + gap-burn list
- [ ] [CS] Golden-path integration suites per world
- [ ] [CS] Seeded action fuzzer with invariant checks

_Depends on: M8-01_

#### M9-02 [AI] Prompt regression suite  · _Should · L_

As a maintainer, I want recorded model interactions replayed in tests so prompt or model upgrades are evaluated, not hoped-for.

**Design notes & edge cases:** Fixture sets per agent from M4-06 (≥10 cases each incl. known-tricky ones: injection attempts, ambiguous commands, low-energy states). Offline replay asserts contract-level correctness (schema, refs, clamps) — not byte equality. Live evaluation script runs the same fixtures against the current Jan model and diffs pass rates; manual gate before any release. Prompt template changes recorded with version notes in the changelog.

**Acceptance criteria:**
- Offline fixture replay runs in the normal test task (no endpoint needed)
- Live eval report compares model/prompt versions side by side
- Known-tricky fixtures documented with why-they're-hard notes

**Subtasks:**
- [ ] [AI] Curate fixture sets per agent (≥10 each) from journals
- [ ] [AI] Live evaluation + diff report script
- [ ] [CS] Wire fixture replay into the test task

_Depends on: M4-06, M5-06_

#### M9-03 [CS] Soak & chaos testing  · _Must · M_

As a player, I want long sessions to be as stable as short ones so a six-hour Sunday doesn't end in a stack trace.

**Design notes & edge cases:** Soak: 500-turn scripted-strategy session with memory/handle sampling (flat RSS required). Chaos matrix: kill Jan mid-dialog / mid-options, corrupt a save mid-session, 8s artificial model latency, malformed responses at random stages, terminal resize storms. Invariant: the turn completes via typed fallback and state stays valid every time.

**Acceptance criteria:**
- 500-turn soak: flat memory, no unhandled exception, final state valid
- Every chaos case ends in typed fallback + valid state (asserted)
- Resize storm leaves layout correct at final width

**Subtasks:**
- [ ] [CS] Soak runner with RSS/handle sampling
- [ ] [CS] Chaos harness (endpoint killer, latency injector, corruptor)
- [ ] [CS] Invariant assertion after every chaotic turn

_Depends on: M9-01_

#### M9-04 [CS] Performance & latency UX  · _Should · M_

As a player, I want the game to feel snappy even when the model is thinking, so AI latency never reads as jank.

**Design notes & edge cases:** Cold start <2s (measured script): defer JIT-heavy paths, lazy-load world second-pass. Debug overlay (F12): per-stage latency breakdown of the last turn. Perceived-latency tricks: Spectre Status spinners with stage labels (“Mia is thinking…”), option prefetch during narrative dwell, warm the cache on boot in dev mode. Tune per-call timeouts vs measured local-model p95.

**Acceptance criteria:**
- Cold-start script: <2s to interactive on reference hardware
- F12 overlay shows real per-stage timings of the last turn
- Prefetch measured to hide ≥60% of option-generation latency typically

**Subtasks:**
- [ ] [CS] Startup profiler + deferral pass
- [ ] [CS] Debug overlay wiring (F12)
- [ ] [AI] Prefetch pipeline + timeout tuning from p95 measurements

_Depends on: M5-06_

#### M9-05 [CS] 1.0 polish & release  · _Should · M_

As a player, I want the final ten percent — splash, themes, docs — so the game feels finished.

**Design notes & edge cases:** Splash/title art in ASCII, default keybindings documented, colorblind-safe theme option (palette pair per severity), README with Jan screenshots + 10-minute path to gameplay, sample configs, CHANGELOG with 1.0 notes, LICENSE. Tag v1.0.0 locally (no distribution — local app).

**Acceptance criteria:**
- README path-to-gameplay verified by a cold reader (<10 min after Jan is running)
- Colorblind-safe theme passes contrast checks for all severity colors
- CHANGELOG documents every milestone mapping M*→features

**Subtasks:**
- [ ] [CS] Splash + keybinding sheet + theme option
- [ ] [CS] README rewrite with screenshots + sample configs
- [ ] [CS] CHANGELOG, LICENSE, v1.0.0 tag

_Depends on: M9-03_

---

## AI agent specifications

### [AI] Action Translator — Translational, temp 0.1

Converts player free text into a canonical action command. Catalog-constrained: the prompt carries the exact list of legal actions/targets at the current location, so hallucination is structurally minimized. Emits a confidence score; below 0.6 the engine asks ONE clarification, then falls back to the menu.

- **Inputs:** Player utterance; Available actions catalog (id, verb, targets, costs); Pinned context (location, time, stats)
- **Output:** { action, targetRef?, params?, confidence: 0..1 } — strict JSON, schema-validated; refs checked against the world whitelist before touching the resolver.
- **Prompt template:** `prompts/translator.md`
- **Fallback:** Drop to the grouped action menu; turn continues normally.

### [AI] Options Generator — Generative, temp 0.6

Proposes 3–6 fresh, context-aware next actions each turn. Must reference only existing actions/entities; engine filters invalid or duplicate items and can top-up from the rule-based generator. Anti-repeat list keeps menus from looping.

- **Inputs:** Location + connections; Player stats/skills/inventory; Clock + NPCs present; Goals + recent actions
- **Output:** [{ label, actionType, targetRef, params?, hint? }] — validated (catalog, refs, preconditions), deduped by (verb,target).
- **Prompt template:** `prompts/options.md`
- **Fallback:** Deterministic rule-based generator enumerates legal actions ranked by goal relevance.

### [AI] Narrator — Generative, temp 0.7

Turns resolved action diffs into 1–2 paragraphs of vivid second-person, present-tense prose grounded in the location sheet, day phase and world tone guide. Forbidden from inventing entities or restating raw stats — both lint-tested.

- **Inputs:** ActionResult diff; Location markdown; Clock/day phase; Tone guide + last 2 narrative beats
- **Output:** Plain prose block (no JSON). Cheap post-lint: unknown proper nouns vs whitelist → warning.
- **Prompt template:** `prompts/narrator.md`
- **Fallback:** Compact rule-based outcome line from ActionResult diffs (shares the offline renderer).

### [AI] NPC Controller — Generative, temp 0.8

Roleplays one NPC per dialog turn using their character sheet (voice, quirks, boundaries), relationship tier and running memory summary. Suggests a relationship delta which the engine clamps to ±5/turn. Memory summary updates after every scene and is part of the save file.

- **Inputs:** NPC character markdown; Relationship level + tier label; NPC memory summary; Situation (where/when/why) + player utterance
- **Output:** { line, mood, intentTag, suggestedRelDelta } — mood maps to NPC state; delta clamped by the engine.
- **Prompt template:** `prompts/npc.md`
- **Fallback:** Mood-keyed canned lines authored per NPC (≥5 each) keep dialogs functional offline.

### [AI] World Director — Simulation, temp 0.4

Adds world autonomy on top of deterministic schedules: proposes up to 3 events per tick (NPC intents, ambient beats, arc progression) after significant elapsed time. Every event is validated against rule-defined envelopes before touching state. Schedules tick even when the model is offline — the world never freezes.

- **Inputs:** Elapsed-time summary; NPC states + schedules; Arc states + world rules
- **Output:** [{ type: NpcIntent|Ambient|ArcBeat, ref, params, narrativeHint }] — budget-capped ≤3, envelope-validated.
- **Prompt template:** `prompts/director.md`
- **Fallback:** Silent deterministic tick: schedule-driven movement only, no events.

## Edge-case & risk register

### R-01 — Malformed JSON from the local model  · _AI · severity High · likelihood High_

Small local models regularly emit truncated, prose-wrapped or schema-violating JSON, especially under long prompts.

**Mitigation:** Strict parse → JsonSchema → 2-attempt repair loop feeding the parser error back to the model → typed failure → per-stage fallback. Model candidates must pass the M0-06 probe gate before being recommended.

### R-02 — Hallucinated entity references  · _AI · severity High · likelihood Med_

Model invents NPCs, locations or items that don't exist, corrupting immersion or crashing naive consumers.

**Mitigation:** RefWhitelist validated engine-side for every AI output (M5-08); catalog-constrained prompting for translator/options; rejections journaled and reused as few-shot negative examples.

### R-03 — Jan not running / endpoint unreachable  · _Ops · severity High · likelihood Med_

Player launches the game without starting the local server, or Jan crashes mid-session.

**Mitigation:** Startup health check → DEGRADED/OFFLINE banner; full rule-based offline mode (M5-07) keeps the game complete and winnable; background probe resumes AI when the endpoint recovers.

### R-04 — Chosen model too weak for structured output  · _AI · severity High · likelihood Med_

A quantized 7B model may roleplay fine but fail strict contracts and multi-step instructions.

**Mitigation:** Model requirements documented (M0-05); probe harness is a hard gate; prompts kept small and single-purpose; split complex reasoning into per-agent narrow contracts.

### R-05 — Context window overflow on long sessions  · _AI · severity Med · likelihood High_

Weeks of in-game history can't fit into a 4k–8k local context.

**Mitigation:** Pinned facts + rolling window + per-NPC summaries (M4-04); token budgeter with eviction policy; overflow marker logged so quality issues are diagnosable.

### R-06 — LLM latency breaks turn flow  · _UX · severity Med · likelihood High_

Local inference can take 5–30s per call; a naive pipeline stalls on every turn.

**Mitigation:** Per-stage timeouts; labeled spinners (“Mia is thinking…”); option prefetch during narrative dwell; dev cache; budgets tuned from measured p95 (M9-04).

### R-07 — Prompt injection via content or input  · _AI · severity High · likelihood Low_

Malicious world markdown, NPC text or player input attempts to override agent instructions.

**Mitigation:** Fixed instruction hierarchy; untrusted content delimited as data; world prompt overrides sandboxed to a placeholder contract (no system-channel access); install-time warning on prompt overrides; red-team fixtures in the regression suite.

### R-08 — Save / world / engine version mismatch  · _Data · severity Med · likelihood Med_

A save outlives the world package version or the engine format it was written with.

**Mitigation:** Three compatibility gates with actionable messages (M6-03); backups before overwrite; world upgrades within same major migrate notes only.

### R-09 — Economy / stat imbalance  · _Content · severity Med · likelihood Med_

Decay rates, salaries and prices interact; a careless world becomes trivially easy or unsurvivable.

**Mitigation:** Headless balance simulator with strategy bots (M8-01); clamp tests; target metric bands enforced before shipping a world.

### R-10 — Spectre markup injection from AI/prose  · _UX · severity Med · likelihood Med_

Square brackets or control characters in model output or world markdown crash or corrupt the renderer.

**Mitigation:** Single escaping choke point in the text pipeline (M3-03/M3-08); hostile-string fixtures run in CI-style tests.

### R-11 — Non-determinism breaks replay & debugging  · _Engine · severity Low · likelihood Med_

AI randomness makes bugs irreproducible without discipline.

**Mitigation:** Seeded RNG; every AI call journaled (request+response); journal replay reproduces state hashes; fixture compiler converts sessions to tests.

### R-12 — Scope creep into agent autonomy  · _Process · severity Med · likelihood High_

It is tempting to keep adding agent behaviors (economy sim, memory graphs, emotions) beyond the 1.0 loop.

**Mitigation:** MoSCoW priorities on every story; milestone exit gates; explicit out-of-scope list (multiplayer, graphics, remote feeds, tool-use agents) reviewed at M9.

## Architecture decision records

### ADR-001 — Single-process .NET 10 console application

- **Context:** The game runs locally only; no deployment, no accounts, one player at a time. Operational simplicity is a feature.
- **Decision:** One console process hosting UI, engine, AI layer and persistence, organized as five library projects under one solution.
- **Rejected alternatives:** Web app + browser UI (rejected: deployment surface for zero benefit); Avalonia GUI (rejected: doubles UI scope); split service + client (rejected: process management burden).

### ADR-002 — Spectre.Console for presentation

- **Context:** Need rich terminal UI: panels, bars, menus, prompts, tables — testable, not hand-rolled ANSI.
- **Decision:** Spectre.Console with a stack-based screen router; all screens render via pure functions verified with TestConsole snapshots.
- **Rejected alternatives:** Terminal.Gui (heavier TUI framework, steeper control model); raw ANSI (zero testability).

### ADR-003 — Markdown + YAML frontmatter as the world format

- **Context:** Worlds must be author-friendly for humans AND directly consumable as LLM context; content should diff well in git.
- **Decision:** One markdown file per entity; YAML frontmatter for structured fields (Markdig + YamlDotNet, strict with warnings); prose body doubles as agent prompt material.
- **Rejected alternatives:** Pure YAML/JSON (prose becomes second-class); SQLite content DB (opaque to authors, no diffs); custom DSL (tooling burden).

### ADR-004 — In-process agents over Microsoft.Extensions.AI — no external orchestrator at runtime

- **Context:** Multi-agent behavior is required, but the tools investigated (opencode-style AGENTS.md multi-agent configs) target coding-assistant workflows, not embedding in a C# game loop. Simplicity and testability dominate.
- **Decision:** Agents = { prompt template + model config + typed output parser + fallback } composed by a small in-process orchestrator over IChatClient. Fake client enables full offline tests. External agent tools may remain a dev-time prompt lab only.
- **Rejected alternatives:** Semantic Kernel / Microsoft Agent Framework (revisit if orchestration outgrows stage-delegate pipelines); external Python agent sidecar (rejected: second runtime, IPC burden).

### ADR-005 — Jan as the local model host via OpenAI-compatible API

- **Context:** Need a free, local, user-friendly LLM server with an OpenAI-compatible surface so the client stays provider-agnostic.
- **Decision:** Target Jan's local server (default http://127.0.0.1:1337/v1). Config is endpoint + model + per-agent ChatOptions, so Ollama/LM Studio work by changing configuration only.
- **Rejected alternatives:** Direct llama.cpp embedding (deployment complexity); cloud APIs (violates local-only requirement).

### ADR-006 — AI proposes, engine disposes

- **Context:** LLM output cannot be trusted with state mutations; hallucination and injection are expected, not exceptional.
- **Decision:** All AI output passes the AIGate: schema validation, RefWhitelist, precondition re-checks and delta envelopes before the resolver applies anything. The model never writes state directly.
- **Rejected alternatives:** Letting the LLM emit state patches or code (rejected outright); trusting model self-validation (contradicts R-01/R-02).

### ADR-007 — Versioned JSON snapshot saves

- **Context:** Saves should be debuggable by hand and resilient; the local single-player scale makes raw performance irrelevant.
- **Decision:** System.Text.Json indented snapshots with formatVersion, checksum footer, atomic tmp→rename writes and rotating autosaves.
- **Rejected alternatives:** SQLite (opaque, overkill); binary serialization (not diffable); event-sourced-only saves (slow load, complexity).

### ADR-008 — Folder/zip .lifeworld packages, local installs only

- **Context:** A NuGet-like story for worlds is desirable for 1.0, but remote feeds, signing and updates are not.
- **Decision:** Deterministic ZIP (.lifeworld) with world.yml + content hashes; install from zip or folder into local libraries; integrity + compatibility gates at install. Remote feeds documented as a post-1.0 candidate.
- **Rejected alternatives:** NuGet feeds as distribution (rejected for 1.0: hosting + auth complexity); git-based sharing (fine as an author workflow, not a runtime dependency).

### ADR-009 — Configuration contracts live in the owning outer layer (Core stays BCL-only)

- **Context:** M0-01 requires LifeSim.Core to stay the innermost, pure domain layer, while M0-03 introduces application settings (LLM endpoint/model, per-agent tuning, paths, UI theme). An initial cut placed the option classes in Core, which pulled Microsoft.Extensions.Options and UI/AI concerns into the domain and only satisfied the architecture test by accident. Binding, precedence and validation all happen at startup in the Console layer.
- **Decision:** All application configuration option contracts live in the outermost layer that owns the configuration lifecycle — currently `LifeSim.Console.Configuration` (`LlmOptions`, `AgentModelOptions`, `AiOptions`, `PathsOptions`, `UiOptions`). Inner layers receive resolved primitive/immutable settings via constructor parameters and never depend on `IOptions` or on the config types. Values that world authors tune come from the world package (M2), not static app config. Core references only the BCL and itself.
- **Rejected alternatives:** Keeping the option classes in Core with an Options package reference (rejected: leaks presentation/AI configuration into the domain and defeats the BCL-only purity gate); a dedicated `LifeSim.Configuration` project (rejected: an extra project and reference graph for a handful of records in a single-process app); defining per-layer option types now (rejected: no inner layer consumes them yet — add narrow records in AI/Persistence only when M4/M6 need them).

### ADR-010 — Serilog for app logging; dedicated JSONL journal for LLM calls

- **Context:** M0-04 needs two different artifacts: a human-readable, size-capped application log, and a machine-consumable record of every LLM interaction for debugging and, later, fixture compilation (M4-06/M9-02). The plan left the writer choice open ("Serilog.Sinks.File or plain rolling writer"). CLI switches must be able to raise verbosity, and content redaction must be possible.
- **Decision:** Application events use Serilog — `Serilog.Sinks.File` for rolling/size-capped/retained files and `Serilog.Sinks.Console` for `--verbose` output. LLM interactions use a dedicated append-only `llm-calls.jsonl`, written by `JsonlLlmCallRecorder` behind an `ILlmCallRecorder` interface and wrapped around `IChatClient` by `RecordingChatClient` (a `DelegatingChatClient`). One record per call (streaming aggregated) with correlationId/agent/model/promptHash/latencyMs/finishReason/success/error/request/response; size-based rotation plus retained-file and directory-byte caps; redaction toggle default off. Core contributes only the ambient `TurnCorrelation`; it takes no logging dependency.
- **Rejected alternatives:** A Serilog JSON formatter for LLM calls (rejected: the logging abstraction is human-oriented, and correlation/agent/token fields are first-class in our schema and awkward to query or replay as fixtures); a hand-rolled rolling writer for app logs (rejected: re-implements rotation/retention Serilog.Sinks.File already provides); Microsoft.Extensions.Logging + OpenTelemetry only (rejected: no durable local file sink without extra providers, and tracing is overkill for a single-process local app); in-memory ring buffer (rejected: loses history between sessions and defeats post-hoc debugging).

### ADR-011 — Hour decay is boundary-based; the clock event dispatcher owns the hour/day seam

- **Context:** M1-01 requires `GameClock` to emit `HourPassed`/`DayStarted` engine events (`plan.md`:190) and M1-02 requires "Decay applied on `HourPassed`" (`plan.md`:207). The shipped code does neither cleanly: `GameClock.Advance` reports `minutes / 60` *chunks* (`src/LifeSim.Core/Time/GameClock.cs`:93) while `dayStarted` is already boundary-based (`newDayIndex > DayIndex`, line 94), and `GameClockEventDispatcher` has zero call sites in `src/` — `ActionResolver.Apply` calls `StatSet.ApplyHourPassed` directly (`src/LifeSim.Core/Actions/ActionResolver.cs`:122–126; tracker D5/D12). The result is partition-dependent decay: two 30-minute actions 14:30→15:00→15:30 yield **zero** ticks for a full elapsed hour, while one 60-minute action 14:30→15:30 yields one. The cheap fix — document the chunk rule — fails because "decay per hour" would then describe something other than the number of hours, balance would depend on how actions are chunked, and two sessions with identical elapsed time but different action granularity would reach different state hashes. The tracker (§5(b)) required this seam to be ruled as one decision because the semantics and the owner are the same question.
- **Decision:** An hour boundary is a clock hour mark (HH:00). **(1) Decay is boundary-based.** `GameClock.Advance` returns `BoundariesCrossed = floor(newTotalMinutes / 60) − floor(TotalMinutes / 60)` (renamed from `HoursPassed`); the count telescopes over any partition of an interval, so the tick count for a given elapsed time is independent of how it was split into actions and needs no carried state. Exactly one decay tick is applied per boundary crossed, carried by the `HourPassed` event. **(2) `GameClockEventDispatcher` owns the advance.** It calls `GameClock.Advance`, raises `HourPassed` once per boundary crossed and `DayStarted` at most once per advance, and returns the advance result. **(3) `WorldState` composes dispatcher → stats.** It owns a private dispatcher and subscribes `Player.Stats.ApplyHourPassed` to `HourPassed` exactly once at construction; `WorldState.AdvanceClock` delegates to the dispatcher, and `ActionResolver.Apply` no longer loops `ApplyHourPassed` itself. The dispatcher stays internal to `WorldState` (no new public engine API); the flow is synchronous, single-threaded and subscription-ordered, so it stays deterministic and offline-testable. **(4) `DayStarted` is unchanged** — still a boolean raised at most once per advance (`plan.md`:195), because it is a notification, not accumulated state; its count is not part of the decay contract.
- **Rejected alternatives:** *Keep `minutes / 60` chunk semantics and document them* (rejected: partition-dependent decay contradicts `decayPerHour`, drifts the balance fuzzer, and makes replay/state-hash depend on action granularity rather than elapsed time); *remainder-carry accumulator* (rejected: partition-independent, but it needs "minutes since the last tick" persisted into world state, the save and the journal — the tick count stops being a pure function of `(start, minutes)`, adding a desync surface and a migration for a property the clock already has); *count elapsed whole hours statelessly without carry* (rejected: impossible — the start's phase offset is not recoverable from `(start, minutes)` alone); *delete `GameClockEventDispatcher` and keep the direct call* (rejected: contradicts the M1-01 subtask "internal event dispatcher publishing `HourPassed`/`DayStarted`" and the M6 autosave/schedule seam, and the question would simply be re-opened when M6 needs it); *wire the dispatcher as a general multi-subscriber event bus now* (rejected for 1.0: only decay subscribes; subscribers are added when a real consumer exists).

### ADR-012 — Pass-out parameters live in `WorldRules`; forced sleep is applied after the advance, never from an event handler

- **Context:** M1-02 requires the pass-out consequence to be *enforced*, not merely reported — "energy 0 → pass out (forced sleep of N hours, mood penalty)" (`plan.md`:207) — and tracked defect D6 confirms `StatConsequence.PassOut` is raised but never acted on (`src/LifeSim.Core/Stats/StatSet.cs`:76–101). Three constraints decide whether N and the penalty may live on `StatDef`, on `StatSet`, or in a world-rules record. **(1) Core is BCL-only and config-free** (ADR-009, `AGENTS.md` §2), so the values cannot come from application configuration — they are world rules. **(2) The DoD forbids new engine API until a markdown-level test proves content can drive it** (`plan.md`:1408), and there is no markdown loader before M2, so expanding `StatDef`/`StatConsequence` now adds unproven per-stat surface. **(3) ADR-011 makes `GameClockEventDispatcher` the owner of the advance and `HourPassed` the decay trigger**, so a handler that advanced the clock from inside a `StatCritical`/`HourPassed` callback would re-enter `AdvanceClock` mid-advance (the re-entrancy constraint recorded in tracker §5(b)). The world package format already reserves a `rules/` folder and M2-06 already scopes "Author rules: needs decay, rent event, goal/win-lose definitions" (`plan.md`:41, 429); `StatDef` is documented as carrying the *consequence selector*, and starvation already keeps its *parameters* (`starvationDrainPerHour`/`starvationDrainTarget`) off `StatDef` as `StatSet` constructor primitives — parameterising a consequence outside the stat definition is established, and a world-rules seam is anticipated rather than speculative.
- **Decision:** **(1) Parameters live in `WorldRules`, not on `StatDef`/`StatSet`.** A BCL-only immutable record `WorldRules` (`LifeSim.Core.Rules`) carries `ForcedSleepHours` (int, default 6, must be ≥ 0), `PassOutMoodPenalty` (decimal, default −10, must be ≤ 0) and `PassOutMoodStatId` (string, default `"mood"`), plus a `Default` singleton. `WorldState` owns one (`WorldRules.Default` when none is supplied). `StatDef` and `StatConsequence` are unchanged: `StatDef` keeps the selector, `WorldRules` carries the parameters. M2-06 populates `WorldRules` from the world's `rules/` content. **(2) The trigger is announced, never acted on inside the handler.** `WorldState` subscribes to the player's `StatSet.StatCritical`; a downward crossing with `Consequence == PassOut` only latches a pending pass-out (the stat id). The handler mutates nothing, so it cannot re-enter the active dispatcher. **(3) Forced sleep is applied after the advance completes.** `ActionResolver.Apply` drains the pending pass-out after its own `AdvanceClock` and journaling, via an internal `WorldState` method: advance by `ForcedSleepHours` hours through the same dispatcher (a multiple of 60 minutes always crosses exactly N boundaries, so exactly N `HourPassed` decay ticks fire — never a hand-rolled decay loop), then apply `PassOutMoodPenalty` to `PassOutMoodStatId` **exactly once**. The triggering action's effects and costs are **not** rolled back or interrupted; pass-out adds time and a penalty, it does not cancel the action, and it does not itself restore energy. **(4) Once per crossing.** The existing `Stat.IsCriticalArmed` semantics already fire a crossing once and re-arm only after recovery above `CriticalAt`; the ruling adds only a re-entrancy guard — the pending latch is cleared *before* the forced-sleep advance, the sleep is driven by the dispatcher directly (not by a nested `AdvanceClock`), and any crossing raised during the forced sleep is deferred to the next advance, so there is no recursion and no re-fire at energy 0. **(5) Journal & determinism.** A pass-out is journaled exactly once as a new `JournalEntryTypes.PassOut` entry (`SimTime` = the clock at which the entity passed out; payload `"<statId>:<sleptHours>"`, e.g. `"energy:6"`), with the ordinary `DayStarted` entry when the sleep rolls a day. Forced sleep is a pure function of `(clock, WorldRules)` — no RNG, no wall clock, no `Guid` — so action-driven replay reproduces the same state hash without replaying the entry. Scope in M1 is the **player** only (NPCs are not turn-simulated yet).
- **Rejected alternatives:** *Extend `StatDef` with optional consequence-payload fields* (rejected: a heterogeneous per-stat payload — `ForcedSleepHours`/`PassOutMoodPenalty` mean nothing unless the consequence is `PassOut` — and it is precisely the new `StatDef` surface the DoD forbids before a markdown loader can drive it); *a `PassOut`-shaped parameter home on `StatSet` mirroring starvation* (rejected: `StatSet` owns no clock, so it cannot express "N hours of time passing", and it would split consequence parameters across two homes); *apply the forced sleep inside the `StatCritical`/`HourPassed` handler* (rejected: re-enters `AdvanceClock` mid-advance, violating ADR-011's re-entrancy constraint and risking double decay); *advance directly by `N × 60` minutes without the dispatcher, or keep a manual `ApplyHourPassed` loop* (rejected: bypasses ADR-011's single advance owner and double-counts decay); *defer D6 to M2* (rejected: M1-02's AC — "Pass-out and starvation consequences covered by unit tests" (`plan.md`:212) — and the M1 exit gate require enforcement now; deferral leaves the gate unreachable).
- **DoD note:** `WorldRules` is new public engine surface added before any markdown test exists. This is a recorded, scoped debt, not an oversight: it is the exact materialization target M2-06 already scopes (`plan.md`:429), it is kept to three fields on one record, and **M2-06 owes the markdown-level test** that populates it from world content. It is the smallest surface that satisfies M1-02; extending `StatDef` would be strictly larger and less content-aligned.

## Definition of done (every story, every milestone)

- [ ] All suites green — including the offline path (fake client, AI disabled)
- [ ] Fallback proven: kill the stage, the turn still completes
- [ ] Validator clean on both shipped worlds
- [ ] Journal + llm-calls written for the change's happy path
- [ ] Docs updated: format reference, AI guidelines, or README as applicable
- [ ] No new engine API without a markdown-level test proving content can drive it

## Explicitly out of scope (1.0)

- Multiplayer or any server component
- Graphics, audio, or non-terminal UI
- Remote world feeds, signing, auto-update
- Cloud LLM providers (violates local-only)
- Tool-use / autonomous agents beyond the five contracts
