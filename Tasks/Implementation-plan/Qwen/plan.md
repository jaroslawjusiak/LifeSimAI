This is a comprehensive and detailed implementation plan for your local, markdown-driven life simulation game engine.

### Technical Clarifications & Recommendations

1. **Markdown Parsing:** Pure Markdown is bad for structured data (stats, IDs). I highly recommend using **YAML Frontmatter** for metadata (stats, traits, IDs) and the **Markdown body** for narrative descriptions. We will use the `YamlDotNet` and `Markdig` libraries.
2. **AI Framework:** You mentioned "OpenCode". In the C# ecosystem, the standard for simple AI integration is the official **OpenAI .NET SDK** (which works perfectly with Jan's OpenAI-compatible API) or **Semantic Kernel** (if you want built-in agent/tool orchestration). To keep it "as simple as possible," this plan uses the **OpenAI .NET SDK** with strict JSON prompting, avoiding heavy framework overhead.
3. **Local LLM Latency:** Local LLMs (via Jan) can be slow. The UI must handle asynchronous streaming or at least show engaging loading animations.

---

## Milestone 1: Project Setup & Core Domain Models

*Goal: Establish the .NET 10 project, UI framework, and foundational data structures.*

* **[CS] Initialize Project and Dependencies**
  * Create .NET 10 Console Application.
  * Add NuGet packages: `Spectre.Console`, `Markdig` (Markdown parsing), `YamlDotNet` (Frontmatter parsing), `OpenAI` (Official .NET SDK for Jan API).
* **[CS] Define Core Domain Models (Data Structures)**
  * Create records/classes for: `WorldDefinition`, `Location`, `Character` (Player & NPC), `Item`, `Trait`, `ActionDefinition`.
  * *Edge Case:* Ensure all models have unique string IDs (e.g., `loc_town_square`) to prevent reference collisions.
* **[CS] Define State Models**
  * Create `GameState`, `CharacterState` (current HP, energy, mood, inventory), `WorldState` (time of day, flags, NPC relationships).

## Milestone 2: Markdown World Loading & Package System

*Goal: Build the system to read, parse, and validate markdown-based game worlds.*

* **[CS] Define Markdown Schema Specification**
  * Define the structure: YAML Frontmatter (ID, Name, Stats, Tags) + Markdown Body (Lore, Descriptions).
  * *Example:*

    ```yaml
    ---
    id: npc_alice
    name: Alice
    base_mood: 50
    traits: [friendly, anxious]
    ---
    Alice is the local baker. She always smells of vanilla...
    ```

* **[CS] Implement Markdown & YAML Parser**
  * Create a `WorldLoader` service that reads `.md` files, splits frontmatter from body, and deserializes into Domain Models.
* **[CS] Implement Package System (Folder-based)**
  * Create a `WorldPackage` structure. A package is a folder containing a `manifest.json` and subfolders (`/locations`, `/npcs`, `/items`, `/rules`).
  * Implement a `PackageResolver` that loads all files in a directory and builds the `WorldDefinition`.
* **[CS] Implement World Validation & Reference Resolution**
  * Write a validator that checks for broken links (e.g., an NPC references an item ID that doesn't exist).
  * *Edge Case:* Handle circular references (Location A leads to B, B leads to A) gracefully without infinite loops during initialization.

## Milestone 3: Game Engine Core (Time, Stats, & Rules)

*Goal: Build the deterministic "physics" of the game engine that runs independently of the AI.*

* **[CS] Implement Time & Turn Manager**
  * Create a `TimeManager` that tracks Game Ticks, Hours, and Days.
  * Implement a `AdvanceTime(ticks)` method that triggers time-based events.
* **[CS] Implement Stat & Modifier Engine**
  * Create a `StatEngine` to handle base stats, current values, min/max bounds, and modifiers.
  * Implement time-decay logic (e.g., energy drops by 5 per hour, hunger drops by 10).
  * *Edge Case:* Ensure stats never drop below 0 or exceed max. Handle "death" or "collapse" states when stats hit 0.
* **[CS] Implement Action Resolution Engine**
  * Create an `ActionResolver` that takes a raw action intent (e.g., "eat apple") and translates it into engine commands (consume item, restore hunger, advance time).
  * *Edge Case:* Handle missing requirements (e.g., player tries to sleep but energy is already 100%).

## Milestone 4: AI Integration & Agent System

*Goal: Connect the engine to Jan (Local LLM) to handle narrative, NPC behavior, and action generation.*

* **[AI] Setup Jan API Client & Connection**
  * Implement a wrapper around the `OpenAI` .NET SDK pointing to Jan's local endpoint (usually `http://localhost:1337/v1`).
  * Implement connection health checks and model selection (allow user to pick which local model Jan is serving).
* **[AI] Design Prompt Templates & System Roles**
  * Create three distinct AI personas via System Prompts:
    1. **The Narrator:** Describes locations, NPC reactions, and outcomes.
    2. **The Action Generator:** Looks at player stats/location and suggests 3-4 logical next actions.
    3. **The Translator:** (Optional, or combined with Generator) Converts free-text player input into structured JSON actions.
* **[AI] Implement Context Window Management**
  * LLMs have limited context. Implement a `ContextBuilder` that feeds the AI: System Prompt + World Rules + Current Location + Player Stats + *Recent History*.
  * *Edge Case:* Implement a "Memory Summarizer" agent. When history exceeds 2000 tokens, use the LLM to summarize past events into a short paragraph to keep the context window clean.
* **[AI] Implement Structured JSON Output & Parsing**
  * Force the LLM to return JSON for actions and state updates using Jan's JSON mode or strict prompting.
  * Create a `JsonSanitizer` to clean up LLM markdown code blocks (e.g., stripping ````json ...````) before `System.Text.Json` deserialization.
  * *Edge Case:* **Crucial:** LLMs *will* hallucinate invalid JSON or invalid actions. Implement a retry mechanism (up to 3 attempts) if JSON parsing fails.

## Milestone 5: Console UI & Gameplay Loop (Spectre.Console)

*Goal: Create the user interface and tie the Engine, AI, and UI together in a playable loop.*

* **[CS] Design Spectre.Console UI Layout**
  * Use `Layout` to create a persistent dashboard:
    * Top: Player Stats (Energy, Hunger, Mood, Money, Time).
    * Middle: Main Narrative Text Area (scrolling).
    * Bottom: Action Selection / Input Area.
* **[CS] Implement the Main Gameplay Loop**
  * Create the `GameLoop` class.
  * Flow: `RenderUI()` -> `WaitForInput()` -> `ProcessAction()` -> `AdvanceTime()` -> `QueryAI()` -> `UpdateState()` -> `RenderUI()`.
* **[CS] Implement AI Interaction & Loading States**
  * When querying the AI, show a Spectre `Spinner` or `Progress` bar (e.g., "Alice is thinking..."). Local LLMs take a few seconds; the UI must not freeze.
  * Use `AnsiConsole.Status()` for async AI calls.
* **[AI] Implement Action Generation & Selection UI**
  * The AI generates a list of suggested actions (JSON array).
  * Render these as a Spectre `SelectionPrompt`.
  * Allow the user to type a custom action (free text). If custom, send to the **Translator Agent** to convert to a valid game action.
* **[CS] Implement Save/Load System**
  * Serialize the `GameState` (Player stats, inventory, world flags, time) to a local JSON file.
  * *Edge Case:* Do not save the AI context/history to the save file to keep it small; reconstruct context from the current game state on load.

## Milestone 6: Edge Cases, Polish, & "Game Feel"

*Goal: Handle the messy reality of LLMs and refine the player experience.*

* **[CS] Implement Action Validation & Fallback**
  * Before applying an AI-suggested action, the Engine must validate it. (e.g., Did the AI suggest "Talk to Bob" when Bob is in a different city?).
  * If invalid, reject the AI action, trigger a Narrator prompt: *"You tried to do X, but Y happened instead,"* and ask for a new input.
* **[AI] Implement "Hallucination Guardrails"**
  * Include a strict rule in the System Prompt: *"You must only use items, locations, and NPCs listed in the provided context. Do not invent new items."*
* **[CS] Implement Event/Trigger System**
  * Create a simple rule engine for Markdown-defined triggers.
  * *Example:* `IF time == 22:00 AND location == bed THEN trigger "sleep_sequence"`. This reduces reliance on AI for basic deterministic game logic.
* **[CS] Add Audio/Visual Feedback (Spectre specific)**
  * Use Spectre's `Markup` for colors (e.g., `[red]You are starving![/]`, `[green]You gained 50 gold.[/]`).
  * Add simple console bell sounds or ASCII art for major events (leveling up, finding rare items).

---

### Recommended Folder Structure for the Codebase

```text
LifeSimEngine/
├── src/
│   ├── LifeSim.Engine/         # Core game logic, stats, time, state
│   ├── LifeSim.AI/             # Jan API client, Prompts, Context management
│   ├── LifeSim.IO/             # Markdown parsing, Package loading, Save/Load
│   └── LifeSim.Console/        # Spectre.Console UI, Main entry point, Game Loop
├── worlds/                     # Default game worlds (Packages)
│   ├── default_human_life/
│   │   ├── manifest.json
│   │   ├── rules/
│   │   ├── locations/
│   │   ├── npcs/
│   │   └── items/
│   └── cyberpunk_survival/     # Example of a different scenario
└── saves/                      # Local save files
```

### Critical Advice for Local LLM Integration

Because you are using a local LLM via Jan:

1. **Model Selection Matters:** Use a model that is good at following JSON instructions and has a decent context window (e.g., Llama-3-8B-Instruct, Mistral-7B-Instruct-v0.3, or Qwen2.5-7B).
2. **Keep Prompts Small:** Local LLMs process tokens slowly. Keep your world descriptions concise. Don't feed the AI a 10,000-word lore document every turn. Feed it *only* the current room description and relevant NPC stats.
3. **Deterministic vs AI:** Use the Engine for math (stats, time, inventory) and the AI for flavor (descriptions, dialog, suggesting actions). Never let the AI do the math (e.g., don't ask the AI to calculate "I have 50 gold, I buy a sword for 30, how much do I have left?"). The Engine must do the math.
