# Solution Design: AI-Powered Console RPG via Jan Local LLM

## 1. Executive Summary

This document outlines the architecture and implementation plan for a text-based RPG console application written in C#. The game utilizes a local LLM hosted on **Jan Server** to drive the narrative, NPC interactions, and dynamic world events. To ensure stability, prevent hallucinations, and maintain game rules, the AI backend is structured as a **Multi-Agent System**.

---

## 2. Multi-Agent System Design

LLMs struggle with context switching (e.g., being creative and calculating math simultaneously). By splitting the AI into specialized agents, we ensure strict rule adherence and high-quality narrative output.

### 2.1 Agent Roster

| Agent Name | Role | Responsibilities |
| :--- | :--- | :--- |
| **Orchestrator (Game Master)** | Router & Flow Controller | Analyzes player input, determines the intent (combat, dialogue, exploration), and routes the request to the appropriate specialized agent. Maintains the high-level game state. |
| **Narrator** | World Builder & Storyteller | Generates descriptive text, sets the scene, describes the outcomes of actions, and maintains the tone/atmosphere of the game. |
| **Arbiter (Rules Engine)** | Mechanics & Logic Judge | Evaluates player actions against game rules. Calculates dice rolls, checks stats, determines success/failure, and calculates damage/loot. Returns structured JSON, not prose. |
| **Persona (NPC Controller)** | Character AI | Adopts specific personalities, memories, and goals for NPCs. Handles dialogue generation and decides NPC reactions/actions based on player choices. |

### 2.2 Agent Interaction Flow

1. Player inputs action.
2. **Orchestrator** classifies intent.
3. If combat/skill check -> **Arbiter** calculates outcome -> **Narrator** describes it.
4. If talking -> **Persona** generates response -> **Narrator** adds environmental flavor.
5. If exploring -> **Narrator** generates the new scene directly.

---

## 3. C# Architecture & Game Engine

### 3.1 Recommended UI Library

**`Spectre.Console`**

* **Why:** It is the undisputed standard for rich C# console applications. It supports colors, tables, panels, progress bars, trees, and markdown rendering. It also has excellent support for asynchronous rendering, which is critical when waiting for LLM responses.

### 3.2 Engine Architecture (Layered Approach)

```text
[ Presentation Layer ]  -> Spectre.Console UI, Input handling, Animations (typing effects)
         |
[ Application Layer ]   -> Game Loop, Agent Orchestrator, Context Window Management
         |
[ Domain Layer ]        -> Entities (Player, Enemy), Stats, Inventory, Action History
         |
[ Infrastructure Layer ]-> Jan API Client, Agent Prompts, State Persistence (JSON file)
```

### 3.3 Core Components

#### A. Game Loop & Flow Control

The game loop must be fully asynchronous to prevent UI freezing during LLM inference.

```csharp
public async Task RunGameLoop()
{
    while (gameState.IsRunning)
    {
        // 1. Render UI (Spectre.Console)
        ui.RenderCurrentState(gameState); 
        
        // 2. Get Player Input
        string playerInput = await ui.GetPlayerInputAsync();
        
        // 3. Process Input via Orchestrator
        var agentResponse = await orchestrator.ProcessInputAsync(playerInput, gameState);
        
        // 4. Update State & History
        gameState.ApplyChanges(agentResponse);
        historyManager.AddToHistory(playerInput, agentResponse);
    }
}
```

#### B. State & History Management

* **`GameState`**: A centralized object holding Player stats, Inventory, Current Location, and Active Quests.
* **`HistoryManager`**: LLMs have limited context windows. The History Manager implements a **Sliding Window + Summarization** strategy.
  * *Recent actions:* Kept in full detail.
  * *Older actions:* Summarized by a lightweight background LLM call (e.g., "You fought goblins and went north").

#### C. Jan Server Integration

Jan exposes an OpenAI-compatible API.

* **Library:** Use the official `OpenAI` .NET SDK or `HttpClient` with `System.Text.Json`.
* **Configuration:** Point the base URL to `http://localhost:1337/v1` (Jan's default local endpoint).

---

## 4. Communication Data Model (JSON)

Communication between the C# engine and the Jan Server relies on the OpenAI Chat Completion format. However, to make the multi-agent system work, we use **Structured Outputs (JSON Mode)** for the Arbiter and Orchestrator.

### 4.1 Standard Chat Request (Sent to Jan)

```json
{
  "model": "your-local-model-name",
  "messages": [
    {
      "role": "system",
      "content": "You are the Arbiter Agent. Evaluate the action strictly based on D&D 5e rules..."
    },
    {
      "role": "user",
      "content": "Player stats: STR 16, HP 20/20. Action: 'I try to kick down the wooden door.'"
    }
  ],
  "response_format": { "type": "json_object" },
  "temperature": 0.2
}
```

### 4.2 Arbiter Agent Response (Structured JSON)

The Arbiter *must* return JSON so the C# engine can update stats without parsing text.

```json
{
  "action_type": "skill_check",
  "skill_used": "athletics",
  "dice_roll": {
    "d20": 14,
    "modifier": 3,
    "total": 17
  },
  "dc_required": 15,
  "success": true,
  "state_changes": {
    "player_hp_change": 0,
    "environment_change": "door_broken"
  },
  "reasoning": "The player's Athletics check (17) beats the DC (15) to break the wooden door."
}
```

### 4.3 Orchestrator Routing Response

When the player types something, the Orchestrator decides who handles it.

```json
{
  "intent": "combat",
  "target_agent": "arbiter",
  "context_for_agent": "Player is attacking the Goblin. Goblin HP: 5/10.",
  "requires_narrator_followup": true
}
```

### 4.4 Final Game State Payload (Internal C# to UI)

How the C# engine packages the final result to display in Spectre.Console.

```json
{
  "narrative_text": "With a mighty swing, your boot splinters the heavy oak door. It crashes inward, revealing a dark, damp corridor.",
  "mechanics_text": "[Athletics Check: 17 vs DC 15 - SUCCESS]",
  "ui_updates": {
    "play_animation": "door_break",
    "play_sound": "wood_splinter",
    "highlight_stat": null
  },
  "next_possible_actions": [
    "Enter the corridor",
    "Peer into the darkness",
    "Check for traps"
  ]
}
```

---

## 5. Implementation Roadmap

### Phase 1: Foundation & UI (Days 1-2)

1. Initialize C# Console App.
2. Install `Spectre.Console`.
3. Build the static UI layout (Panels for Stats, Main Text Area, Input Prompt).
4. Implement basic local `GameState` and `Player` classes.

### Phase 2: Jan Integration & Single Agent (Days 3-4)

1. Set up Jan Server, download a capable local model (e.g., Llama-3-8B-Instruct or Mistral).
2. Create the `JanApiClient` using `HttpClient`.
3. Implement the **Narrator Agent** (Standard text generation).
4. Create a basic game loop where the player can type, and the Narrator responds.

### Phase 3: Multi-Agent & Structured Data (Days 5-7)

1. Implement the **Orchestrator Agent** (Intent classification via JSON mode).
2. Implement the **Arbiter Agent** (Rules engine via JSON mode).
3. Wire up the C# engine to parse Arbiter JSON and update `GameState` (e.g., deduct HP).
4. Implement the **Persona Agent** for specific NPC encounters.

### Phase 4: Context Management & Polish (Days 8-10)

1. Implement `HistoryManager` (Sliding window for the prompt context).
2. Add Spectre.Console animations (e.g., `Markup` for colors, `Live` displays for typing effects).
3. Add state persistence (Save/Load game to a local JSON file).
4. Playtesting and prompt engineering (tweaking system prompts for better RPG behavior).

---

## 6. Key Technical Considerations

1. **Latency Handling:** Local LLMs can take 2-10 seconds to respond. Use Spectre.Console's `Status` (spinner) or a streaming API approach to show text generating in real-time so the UI doesn't feel frozen.
2. **Model Selection:** For a multi-agent system, you need a model good at following JSON formatting instructions. **Llama-3-8B-Instruct** or **Mistral-7B-Instruct-v0.3** are highly recommended for Jan.
3. **Prompt Engineering:** The `System` prompts for each agent must be strictly separated. The Arbiter prompt should explicitly forbid narrative text and demand strict JSON. The Narrator prompt should be forbidden from making game rule decisions.
4. **Fallback Mechanisms:** If the LLM returns malformed JSON (which happens), the C# engine must have a `try/catch` fallback to re-prompt the agent or apply a default "fail" state without crashing the game.
