# Simple LLM-Powered Console RPG — Implementation Plan & Solution Design

A C# console RPG driven by a **local Jan server** (OpenAI-compatible API). C# owns game state, rules, RNG, and persistence. The LLM layer is a **multi-agent system** that narrates, roleplays, and *proposes* outcomes — never mutates stats directly.

---

## 1. Goals and constraints

| Goal | Constraint |
| ------ | ------------ |
| Playable text RPG loop: explore, talk, fight, loot, quest | Fairly simple — not a full CRPG engine |
| Local LLM via Jan | OpenAI-compatible HTTP (`/v1/chat/completions`) |
| Multi-agent narration & world consistency | Agents propose; C# validates and commits |
| Rich console UX | Spectre.Console |
| Replayable history + save/load | JSON persistence; C# is source of truth |

**Non-goals (v1):** real-time combat, graphics, multiplayer, tool-calling plugins beyond JSON schemas, cloud models.

---

## 2. High-level architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Presentation — Spectre.Console                             │
│  Narrative panel · Stats · Inventory · Log · Prompts        │
└──────────────────────────┬──────────────────────────────────┘
                           │ IGameUi / commands
┌──────────────────────────▼──────────────────────────────────┐
│  Game Engine (C#)                                           │
│  GameLoop · TurnProcessor · RuleEngine · Dice · Persistence │
│  GameState (source of truth) · ActionHistory · Chronicle    │
└──────────────┬───────────────────────────────┬──────────────┘
               │ Intent + snapshot             │ Validated mutations
┌──────────────▼──────────────┐   ┌────────────▼──────────────┐
│  Agent Orchestrator         │   │  Domain model             │
│  Router → specialist agents │   │  Character, Location,     │
│  JSON contracts             │   │  Item, Quest, Encounter   │
└──────────────┬──────────────┘   └───────────────────────────┘
               │ Chat Completions + JSON schema
┌──────────────▼──────────────┐
│  JanClient (HttpClient)     │
│  localhost OpenAI API       │
└─────────────────────────────┘
```

**Principle:** LLMs never write HP, gold, inventory, or quest flags. They emit structured *proposals*. The RuleEngine accepts, rejects, or clamps them, then the Narrator describes what actually happened.

---

## 3. Multi-agent system

Keep the roster small. Seven agents cover a simple RPG without a swarm of redundant calls.

### 3.1 Agent roster

| Agent | Role | When invoked | Responsibilities | Must not |
| ------- | ------ | -------------- | ------------------ | ---------- |
| **Orchestrator (Router)** | Traffic cop | Every player turn | Classify intent; choose agent pipeline; assemble context window; merge specialist JSON into one `TurnProposal`; call Narrator last | Invent world facts; change stats |
| **Narrator (Dungeon Master voice)** | Prose | End of every turn; scene intros | Immersive description of *committed* state; 2nd-person present; short, punchy; mark UI hints (`[location]`, `[mood]`) | Contradict GameState; invent loot/stats |
| **World Keeper** | Continuity | Move / look / time / environment | Locations, exits, time of day, weather, discovered flags; propose `WorldDelta` | Move items between inventories; kill characters |
| **NPC Director** | Roleplay | Talk / social / shop | Speak in-character; personality, knowledge limits, disposition; propose dialogue + `SocialDelta` (disposition, rumors, shop intent) | Omniscience; grant items/quests without IDs that exist in state |
| **Combat Referee** | Tactical colour | Combat rounds | Describe intent of enemies; propose target, tactic, flavour; suggest difficulty tags | Roll dice; apply damage; decide hits |
| **Rules Advisor** | Mechanics language | Skill checks, combat, crafting | Map free-text intent → `CheckRequest` (skill, DC band, opposed or not); list legal actions from state | Override RuleEngine; fudge numbers |
| **Chronicler** | Memory | After N turns or scene change | Compress ActionHistory into a chronicle (facts only); extract lasting consequences | Purple prose; overwrite canonical state |

Optional later (not v1): Loot Smith (item flavour names), Quest Weaver (multi-beat arcs), Encounter Designer (random encounters).

### 3.2 Orchestration pipeline

```
Player input (free text)
        │
        ▼
┌───────────────┐
│ Orchestrator  │  intent: Explore | Talk | Combat | Inventory | Rest | System
│ (cheap/fast)  │  entities mentioned, location, urgency
└───────┬───────┘
        │
        ├── Inventory / System  → C# only (no LLM) if fully deterministic
        │
        ├── Explore  → World Keeper → Rules Advisor? → RuleEngine → Narrator
        ├── Talk     → NPC Director → (World Keeper if location facts) → RuleEngine → Narrator
        ├── Combat   → Rules Advisor → Combat Referee → RuleEngine (dice) → Narrator
        └── Rest     → Rules Advisor → RuleEngine → Narrator
        │
        ▼
 Chronicler (async / every K turns or on scene change)
```

**Routing rules**

- Prefer **one specialist + Narrator** per turn. Parallelize World Keeper + NPC Director only when talking *and* moving.
- Use a **smaller/faster model** (or tighter max_tokens) for Orchestrator and Chronicler; larger for Narrator / NPC Director if Jan hosts multiple models.
- If Orchestrator confidence is low, default pipeline: Rules Advisor → Narrator (safe exploration).
- Hard timeout per agent (e.g. 20s); on failure, C# fallback: “Nothing obvious happens.” + log error — never block the loop forever.

### 3.3 Context each agent receives

Always a **trimmed snapshot**, not the full save file:

- Player: name, class, level, HP/max, resources, notable traits, inventory *names* (not every modifier)
- Location: id, name, short desc, exits, present NPCs/items (ids + display names)
- Active quests (id, stage, 1-line objective)
- Last 6–10 turns of ActionHistory (player text + committed outcome one-liners)
- Chronicle (compressed long-term memory, ~500–800 tokens)
- Agent-specific: NPC sheet, encounter roster, legal actions list

System prompts are **versioned files** (`Prompts/narrator.v1.md`, etc.), not hardcoded in C# beyond a loader.

### 3.4 Agent contract (shared)

Every specialist returns JSON matching a schema. Temperature: Orchestrator 0.1–0.3, Rules 0.2, World/NPC 0.6–0.8, Narrator 0.8–1.0, Chronicler 0.2.

If JSON parse fails: one retry with “return valid JSON only”; then fallback.

---

## 4. C# solution design

### 4.1 Recommended UI library

**Spectre.Console** (plus `Spectre.Console.Json` if you want pretty debug dumps).

Why: panels, tables, markup, live layouts, status spinners (while Jan thinks), selection prompts, markdown-ish markup, Figlet titles, progress for rest/travel. It is the standard for rich C# console games/tools.

Alternatives (not recommended for v1): Terminal.Gui (full TUI, heavier), raw ANSI, Spectre.Console.Cli (great for *tools*, not a game loop).

**Layout sketch**

```
┌─ THE WILDS ──────────────  Day 3, dusk ─┐
│ The path narrows. A hooded figure…      │  ← Narrative (Panel, wrapped)
│                                         │
├─ You ──────────────────────┬─ Party ────┤
│ Kael  HP 12/20  Gold 40    │ (later)    │
│ Sword, 2 potions           │            │
├────────────────────────────┴────────────┤
│ > talk to the hooded figure             │  ← TextPrompt
└─────────────────────────────────────────┘
 Log: [Check] Perception 14 vs DC 12 — success
```

Use `AnsiConsole.Live` or a redraw of a `Layout` each turn. Spinner: `AnsiConsole.Status()` during LLM calls.

### 4.2 Project structure

```
src/
  Rpg.Domain/           # entities, value objects, GameState — no IO
  Rpg.Engine/           # loop, rules, dice, history, save/load interfaces
  Rpg.Agents/           # orchestrator, agent runners, prompts, JSON DTOs
  Rpg.Infrastructure/   # Jan HttpClient, JSON file persistence
  Rpg.ConsoleApp/       # Spectre UI, composition root
tests/
  Rpg.Engine.Tests/
  Rpg.Agents.Tests/     # parse/schema tests with canned JSON
```

Target: **.NET 8+**. Nullable enabled. `System.Text.Json` source generation for agent DTOs.

### 4.3 Domain (source of truth)

```csharp
// Core identifiers are stable strings (e.g. "loc.forest.crossroads")
record CharacterId(string Value);
record LocationId(string Value);
record ItemId(string Value);
record NpcId(string Value);
record QuestId(string Value);

sealed class Character
{
    public CharacterId Id { get; }
    public string Name { get; }
    public string Archetype { get; }          // warrior, rogue, mystic
    public int Level { get; private set; }
    public int Hp { get; private set; }
    public int HpMax { get; private set; }
    public int Gold { get; private set; }
    public Attributes Attr { get; }           // Str, Dex, Int, Cha, Con, Wis
    public List<Skill> Skills { get; }
    public Inventory Inventory { get; }
    public List<StatusEffect> Effects { get; }
    // Mutators only via methods: ApplyDamage, Heal, AddItem, ...
}

sealed class GameState
{
    public int Turn { get; private set; }
    public WorldTime Time { get; private set; }
    public LocationId CurrentLocationId { get; private set; }
    public Character Player { get; }
    public Dictionary<LocationId, Location> World { get; }
    public Dictionary<NpcId, Npc> Npcs { get; }
    public Dictionary<QuestId, Quest> Quests { get; }
    public Encounter? ActiveEncounter { get; private set; }
    public GameFlags Flags { get; }           // discovered_*, npc_*_dead, etc.
}

sealed class ActionHistory
{
    public IReadOnlyList<TurnRecord> Turns { get; }
}

sealed class TurnRecord
{
    public int Turn { get; init; }
    public DateTimeOffset At { get; init; }
    public string PlayerInput { get; init; }
    public Intent Intent { get; init; }
    public IReadOnlyList<CheckResult> Checks { get; init; }
    public IReadOnlyList<StateChange> Changes { get; init; }  // audit log
    public string Narration { get; init; }
    public string OutcomeSummary { get; init; }               // one line for context
}

sealed class Chronicle
{
    public string Summary { get; private set; }  // compressed facts
    public int UpToTurn { get; private set; }
}
```

**Inventory / items:** template catalog (static JSON content) + instance ids. LLM may *flavour* a generated loot name only after RuleEngine rolls a loot table.

### 4.4 Game engine architecture

```
GameLoop
  1. Render(state)
  2. input = UI.Read()
  3. if slash-command (/save /inv /help /quit) → handle in C#, continue
  4. intent = Orchestrator.Classify(input, snapshot)   // or local keyword fallback
  5. proposal = Orchestrator.RunPipeline(intent, snapshot)
  6. resolution = RuleEngine.Resolve(proposal, state)  // dice, clamps, legality
  7. state.Apply(resolution.Changes)
  8. narration = Narrator.Describe(resolution, state)  // or use proposal.narration if already conditioned on resolution
  9. history.Append(...)
 10. maybe Chronicler.Update()
 11. auto-save (optional)
```

**Important split for Narrator:** Prefer **two-phase**:

1. Specialists → `TurnProposal` (no flavour text, or flavour marked unofficial)
2. RuleEngine → `TurnResolution` (facts: hit, 7 damage, goblin HP 3, item gained)
3. Narrator sees **resolution only** and writes prose

That prevents “you slay the dragon” when the hit missed.

**RuleEngine** (pure C#):

- Skill check: `d20 + skill + mods` vs DC (DC from proposal band: Easy 10 / Medium 14 / Hard 18, clamped)
- Combat: initiative once; attack roll vs defense; damage dice from weapon table; death at 0 HP
- Movement: exit must exist and not be blocked
- Economy: prices from NPC shop table
- Illegal proposal → reject that part, keep the rest, flag `partial`

**Dice:** `IRandom` injectable for tests.

**CommandInterpreter (local, no LLM):** `/save`, `/load`, `/stats`, `/inv`, `/log`, `/help`, `/quit`, `/debug json`. Free text always goes to agents.

### 4.5 Engine interfaces

```csharp
interface IGameUi
{
    Task RenderAsync(GameViewModel vm, CancellationToken ct);
    Task<string> ReadInputAsync(CancellationToken ct);
    IDisposable ShowThinking(string status);
}

interface IAgentOrchestrator
{
    Task<IntentResult> ClassifyAsync(string input, StateSnapshot snap, CancellationToken ct);
    Task<TurnProposal> ProposeAsync(IntentResult intent, string input, StateSnapshot snap, CancellationToken ct);
    Task<string> NarrateAsync(TurnResolution resolution, StateSnapshot snap, CancellationToken ct);
    Task<string> CompressChronicleAsync(IReadOnlyList<TurnRecord> recent, string previous, CancellationToken ct);
}

interface IRuleEngine
{
    TurnResolution Resolve(TurnProposal proposal, GameState state);
}

interface IGameStore
{
    Task SaveAsync(GameState state, ActionHistory history, Chronicle chronicle, CancellationToken ct);
    Task<(GameState, ActionHistory, Chronicle)?> LoadAsync(string slot, CancellationToken ct);
}
```

### 4.6 Jan integration

Jan exposes OpenAI-compatible:

`POST http://127.0.0.1:1337/v1/chat/completions`  
(confirm port/model in Jan settings; make base URL + model id configurable)

```csharp
sealed class JanOptions
{
    public Uri BaseUrl { get; init; } = new("http://127.0.0.1:1337/v1/");
    public string ChatModel { get; init; } = "local-model";
    public string FastModel { get; init; } = "local-model"; // same if only one
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
}

sealed class JanChatClient
{
    // HttpClient + System.Net.Http.Json
    // chat.completions: messages[], temperature, max_tokens,
    // response_format = { type: "json_object" } when Jan/model supports it
}
```

**Resilience:** retries on 429/5xx (2x), circuit if Jan is down → “LLM offline” mode with canned templates + pure RuleEngine (bare bones but playable).

**Token budget:** snapshot builder enforces max chars per section; drop oldest history first; never drop current location + player vitals.

### 4.7 Content vs runtime

Ship **content packs** as JSON (not in the LLM’s head):

- `world/locations.json` — graph of locations
- `world/npcs.json` — personality, knowledge keys, shop tables
- `world/items.json` — weapons, armor, potions
- `world/encounters.json` — enemy groups, DCs
- `world/quests.json` — stages, success flags

LLM improvises *within* those ids. World Keeper may propose `discover: loc.xyz` only if that id exists **or** (v1.1) a `GeneratedLocation` gated by a flag `allowProceduralPlaces`.

v1 recommendation: **fixed map**, LLM fills description colour, not topology.

---

## 5. Communication data model

### 5.1 Envelope (C# → Jan)

Standard Chat Completions. System + user. User payload is always JSON so agents stay structured.

```json
{
  "model": "local-model",
  "temperature": 0.7,
  "max_tokens": 700,
  "response_format": { "type": "json_object" },
  "messages": [
    {
      "role": "system",
      "content": "You are the World Keeper for a text RPG. Reply with JSON only matching the schema. Never invent location ids."
    },
    {
      "role": "user",
      "content": "{ ... StateSnapshot + player input ... }"
    }
  ]
}
```

### 5.2 StateSnapshot (sent to agents)

```json
{
  "turn": 17,
  "time": { "day": 3, "phase": "dusk" },
  "player": {
    "id": "pc.kael",
    "name": "Kael",
    "archetype": "rogue",
    "level": 2,
    "hp": 12,
    "hpMax": 20,
    "gold": 40,
    "attributes": { "str": 10, "dex": 16, "int": 12, "cha": 11, "con": 12, "wis": 13 },
    "skills": { "stealth": 5, "perception": 3, "persuasion": 1 },
    "inventory": [
      { "id": "item.dagger.iron", "name": "Iron dagger", "qty": 1 },
      { "id": "item.potion.health.minor", "name": "Minor health potion", "qty": 2 }
    ],
    "effects": []
  },
  "location": {
    "id": "loc.forest.crossroads",
    "name": "Forest Crossroads",
    "tags": ["outdoor", "road", "forest"],
    "exits": [
      { "dir": "north", "to": "loc.village.gate", "label": "village road" },
      { "dir": "east", "to": "loc.forest.deep", "label": "dark path" }
    ],
    "npcsHere": [
      { "id": "npc.hooded.wanderer", "name": "Hooded wanderer", "disposition": 0 }
    ],
    "itemsHere": []
  },
  "quests": [
    { "id": "q.missing.child", "stage": "ask_around", "brief": "A child vanished near the woods." }
  ],
  "flags": ["discovered.village", "met.hooded.wanderer"],
  "encounter": null,
  "chronicle": "Kael left Graybrook after taking the missing-child rumor. Met a hooded wanderer at the crossroads who hinted the east path is watched.",
  "recentTurns": [
    {
      "turn": 16,
      "player": "I look around the crossroads",
      "summary": "Perception success. Fresh tracks lead east. Hooded figure remains by the milestone."
    }
  ],
  "playerInput": "talk to the hooded figure about the missing child"
}
```

### 5.3 Orchestrator output — IntentResult

```json
{
  "intent": "Talk",
  "confidence": 0.86,
  "targets": {
    "npcIds": ["npc.hooded.wanderer"],
    "locationIds": [],
    "itemIds": [],
    "questIds": ["q.missing.child"]
  },
  "pipeline": ["NpcDirector", "RulesAdvisor", "Narrator"],
  "notes": "Social inquiry; may require persuasion check"
}
```

`intent` enum: `Explore`, `Talk`, `Combat`, `UseItem`, `Inventory`, `Rest`, `Travel`, `System`, `Unknown`.

### 5.4 TurnProposal (specialists merged)

```json
{
  "schemaVersion": 1,
  "intent": "Talk",
  "checks": [
    {
      "id": "chk.1",
      "skill": "persuasion",
      "dcBand": "medium",
      "reason": "Wanderer is secretive about the woods"
    }
  ],
  "npc": {
    "speakerId": "npc.hooded.wanderer",
    "line": "The east path remembers little feet… and something that walks behind them.",
    "dispositionDelta": 1,
    "revealedKnowledge": ["rumor.east.path.watched"]
  },
  "world": {
    "timeAdvance": "none",
    "discoverFlags": [],
    "moveTo": null
  },
  "combat": null,
  "loot": [],
  "questUpdates": [
    { "questId": "q.missing.child", "setStage": "follow_east_tracks" }
  ],
  "illegalOrUnclear": false,
  "playerFacingHint": "The wanderer might say more if pressed — or if coin changes hands."
}
```

Combat variant (proposal only — no numbers that stick):

```json
{
  "schemaVersion": 1,
  "intent": "Combat",
  "checks": [
    { "id": "atk.player", "kind": "attack", "weaponId": "item.dagger.iron", "targetId": "npc.wolf.1" }
  ],
  "combat": {
    "action": "attack",
    "actorId": "pc.kael",
    "targetId": "npc.wolf.1",
    "tactic": "flank",
    "flavour": "low slash toward the foreleg"
  },
  "world": { "timeAdvance": "none", "discoverFlags": [], "moveTo": null },
  "npc": null,
  "loot": [],
  "questUpdates": [],
  "illegalOrUnclear": false
}
```

### 5.5 TurnResolution (C# → Narrator, never from LLM)

```json
{
  "turn": 17,
  "ok": true,
  "partial": false,
  "checks": [
    {
      "id": "chk.1",
      "skill": "persuasion",
      "roll": 14,
      "mod": 1,
      "total": 15,
      "dc": 14,
      "success": true
    }
  ],
  "changes": [
    { "op": "NpcDisposition", "npcId": "npc.hooded.wanderer", "delta": 1, "newValue": 1 },
    { "op": "SetFlag", "flag": "rumor.east.path.watched" },
    { "op": "QuestStage", "questId": "q.missing.child", "stage": "follow_east_tracks" }
  ],
  "npcLine": "The east path remembers little feet… and something that walks behind them.",
  "rejected": [],
  "publicSummary": "Persuasion 15 vs 14 success. Wanderer hints the east path is watched. Quest updated."
}
```

### 5.6 Narrator output

```json
{
  "title": "A warning at dusk",
  "prose": "The wanderer’s face stays in shadow, but their voice is dry as old leaves. \"The east path remembers little feet… and something that walks behind them.\" A crow startles from the milestone. The dark path suddenly feels closer than it was a moment ago.",
  "ui": {
    "mood": "ominous",
    "locationLabel": "Forest Crossroads"
  }
}
```

### 5.7 Chronicler output

```json
{
  "upToTurn": 17,
  "summary": "Kael (rogue) left Graybrook on a missing-child rumor. At the forest crossroads a hooded wanderer, now cautiously friendly, warned that the east path is watched and that something follows small tracks. Quest: follow east tracks."
}
```

### 5.8 Save file (disk)

```json
{
  "schemaVersion": 1,
  "savedAt": "2026-03-22T18:04:00Z",
  "state": { "...full GameState..." },
  "history": { "turns": [ "...TurnRecord..." ] },
  "chronicle": { "upToTurn": 17, "summary": "..." }
}
```

Keep last 100 turns in the save; older turns exist only inside chronicle.

---

## 6. Game flow (runtime)

```
[Boot]
  Load content packs → optional /load slot → character create (Spectre prompts, C#)
  Jan health check GET /v1/models
  Opening scene: Narrator(resolution = intro facts)

[Loop]
  Draw UI
  Read line
  Slash command? handle, continue
  Snapshot = StateSnapshotBuilder.Build(state, history, chronicle)
  Intent = Orchestrator.Classify
  if Inventory-only deterministic command: RuleEngine, skip LLM specialists
  else Proposal = pipeline(specialists)
  Resolution = RuleEngine.Resolve  // dice happen here
  Apply resolution to GameState
  Prose = Narrator(resolution)
  Append TurnRecord
  If turn % 8 == 0 or location changed: Chronicle = Chronicler(...)
  Autosave slot "current"

[Combat]
  ActiveEncounter set by RuleEngine when enemies engage
  Each player input is one round; Combat Referee + Rules Advisor
  Initiative stored on Encounter
  On all enemies down: loot table (C#) → optional flavour names (Narrator)
  Encounter cleared

[Death]
  HP 0 → game over panel, offer load
```

**Turn budget (feel):** player should wait once per action. Parallel HTTP for World+NPC when needed. Show Status spinner: “The wanderer considers your words…”.

---

## 7. Prompt design guidelines

- Each agent: role, output schema, **forbidden behaviors**, few-shot (1–2 tiny examples).
- Inject: “Canonical ids: …” listing legal location/npc/item ids for this snapshot.
- Narrator: “Describe only facts in TurnResolution. Do not add items, wounds, or travel.”
- NPC Director: “You know only knowledgeKeys on your sheet plus public flags.”
- Max prose ~120–180 words per turn (console comfort).
- Language: match player language; default English.

Store prompts as files; include `{schema}` placeholder filled by C#.

---

## 8. Implementation plan

### Phase 0 — Spike (1–2 days)

- Empty Spectre app: layout, prompt, fake narration.
- JanClient: list models, one chat completion, JSON parse.
- Config: `appsettings.json` (`Jan:BaseUrl`, `Jan:Model`).

### Phase 1 — Engine without LLM (2–4 days)

- Domain + content packs (one village, forest, 2 NPCs, 1 quest, 1 wolf fight).
- GameLoop, slash commands, save/load.
- RuleEngine: travel, skill check, combat, inventory, rest.
- Parser for a tiny verb set (`go north`, `attack wolf`, `talk wanderer`) so the game is playable **offline**.
- Tests for dice/combat/inventory.

### Phase 2 — Agents (3–5 days)

- DTOs + JSON schemas + prompt files.
- Orchestrator classify + pipelines.
- Wire World Keeper, NPC Director, Rules Advisor, Narrator, Chronicler, Combat Referee.
- Snapshot builder with token budgets.
- Fallback if Jan down (Phase 1 verb parser).

### Phase 3 — UX polish (1–2 days)

- Live layout, colors by mood, combat table, log of checks.
- Character creation wizard.
- `/debug` last JSON request/response (behind flag).

### Phase 4 — Content & balance (ongoing)

- Second area, shops, 2–3 quests.
- Tune DCs, prompt leakage, chronicle quality.
- Optional second model for router.

**Milestone “fun”:** talk to NPC → check → quest update → travel → fight → loot, all narrated, save/load works.

---

## 9. Configuration example

```json
{
  "Jan": {
    "BaseUrl": "http://127.0.0.1:1337/v1/",
    "ChatModel": "llama-3.1-8b-instruct",
    "FastModel": "llama-3.1-8b-instruct",
    "TimeoutSeconds": 45
  },
  "Game": {
    "HistoryTurnsInContext": 8,
    "ChronicleEveryTurns": 8,
    "Autosave": true,
    "AllowProceduralPlaces": false
  }
}
```

---

## 10. Testing strategy

| Layer | How |
| ------- | ----- |
| RuleEngine | Unit tests, seeded RNG |
| JSON contracts | Schema tests; golden files |
| Orchestrator | Fake `IChatClient` returning canned JSON |
| Snapshot builder | Assert ids present, size under budget |
| UI | Manual; optional Spectre test console |
| Playtest | Scripted transcript: 20 turns through the main quest |

---

## 11. Risks and mitigations

| Risk | Mitigation |
| ------ | ------------ |
| Hallucinated items/places | Closed id lists; RuleEngine drops unknown ids |
| Narrator contradicts dice | Two-phase: narrate *after* resolution |
| Slow local model | Fast model for router; short max_tokens; spinner; offline verbs |
| Context overflow | Chronicle + last N turns; snapshot trim |
| JSON failures | `json_object` mode; retry; skip agent |
| Player tries to jailbreak the DM | RuleEngine ignores “I deal 999 damage”; prompts forbid granting wishes |
| Save bloat | Cap history; chronicle |

---

## 12. Suggested NuGet packages

- `Spectre.Console`
- `Microsoft.Extensions.Hosting` (DI, config, logging)
- `Microsoft.Extensions.Http` (typed Jan client)
- `System.Text.Json` (built-in)
- `FluentValidation` (optional, proposals)
- Test: `xunit`, `FluentAssertions`, `NSubstitute`

---

## 13. What “simple” looks like in v1

- One region, ~8 locations, 4 NPCs, 2 enemy types, 1 main quest + 1 side rumor.
- Free-text input with LLM; slash commands always work.
- Agents: Orchestrator, Narrator, World Keeper, NPC Director, Combat Referee, Rules Advisor, Chronicler.
- C# owns HP, gold, inventory, map graph, dice, saves.
- Spectre.Console for a readable, atmospheric loop.

That split keeps the fantasy in the model and the game in the engine.
