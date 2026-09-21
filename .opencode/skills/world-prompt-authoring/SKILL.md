---
name: world-prompt-authoring
description: |
  Use when writing world content or agent prompt overrides for LifeSim Engine: character
  sheets, tone guides, prompt templates with placeholders, grounding/injection rules, and
  authoring docs. Covers M8-02/03/04 and the content that grounds the five agents. Not for
  loader/validator code (see markdown-ast-validator).
---

# World & Prompt Authoring in LifeSim Engine

Content is the game. Mechanics live in C#; tone, voice and lore live in markdown
([ADR-003](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1334)). This
skill is about writing content that **grounds** the model instead of inviting hallucination.

---

## 1. Grounding Rules

1. **One markdown file per entity.** Frontmatter = structured fields; body = prose the
   agents read. See the format reference
   ([M8-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1078)).
2. **Narrators are grounded by what you write.** A location needs sensory specifics
   (smells, objects, light) so the Narrator has facts to reuse instead of inventing them.
3. **No invented proper nouns.** The model may only reference ids/names in the world text;
   the name-lint checks against the `RefWhitelist`
   ([M5-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L713)).
4. **Stats never appear as raw numbers in prose.** "You are exhausted" is content;
   "energy −35" is the outcome formatter's job.
5. **Untrusted text is data.** Player input, world prose and NPC lines are delimited and
   must never read as instructions (see `llm-redteam-evaluator`).

---

## 2. Character Sheet Anatomy ([M8-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1061))

A good sheet gives the NPC controller enough to stay in voice across moods and to survive
offline. Required elements:

- **Identity**: `id`, `name`, `age`, `occupation`, `homeLocation`.
- **Voice samples**: 3+ lines of actual dialogue showing rhythm, vocabulary, verbal tics.
- **Quirks & boundaries**: what they never talk about, how they deflect.
- **Schedule**: the compact DSL (`mon-fri 09-17 office`, overnight ranges supported).
- **Offline lines**: **≥ 5 mood-keyed canned lines** so the dialog works with AI off
  ([M5-07](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L813)).

```markdown
---
id: barista_mia
name: Mia Alvarez
age: 27
occupation: Barista
homeLocation: apartment_block
traits: [warm, overworked, observant]
schedule:
  - mon-fri 06-14 cafe
  - sat 08-12 park
  - "sun 00-24 apartment_block"
relationshipStart: 5
---

## Voice
Warm, fast, teases with affection. Calls the player "hon" when comfortable.

## Speech Patterns
- Starts sentences with "Look," when she is being sincere.
- Never finishes a sentence about her ex.
- Uses coffee metaphors for people.

## Sample Lines
"You look like a decaf kind of morning, hon."
"Look, I'm not saying he's wrong. I'm saying he ordered an espresso at 8 p.m."
"Come back when you can taste something other than deadlines."

## Boundaries
Won't discuss her ex. Gets clipped when asked about money.

## Offline Lines (mood-keyed)
happy: "Double shot? You read my mind."
neutral: "The usual, hon?"
tired: "Long shift. I might be speaking in steam."
sad: "…Just coffee today, okay?"
angry: "Seriously? At this hour?"
```

---

## 3. Tone Guide (`world.md`)

The world overview sets the register every agent inherits. Keep it short, concrete and
prescriptive.

```markdown
## Tone Guide
- Second person, present tense. Never address the player as "you, the player".
- Grounded realism with dry humour; no fantasy, no melodrama.
- Weather and time of day colour every scene.
- Consequence is shown, not stated: rent is a letter under the door, not a debit line.
```

---

## 4. Prompt Overrides ([M4-05](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L658))

Worlds may override engine defaults under `prompts/<agent>.md`. Overrides are
**placeholder-only and sandboxed** — they cannot touch the system channel and cannot add
unknown placeholders (a validator error at load time, see `markdown-ast-validator`).

```markdown
{{#ToneGuide}}
## World Tone
{{ToneGuide}}
{{/ToneGuide}}

You are the Narrator. Describe what just happened to the player in 1–2 paragraphs.
Ground every detail in the location and the time of day. Do not invent named people,
places or objects. Do not restate raw stat numbers.

## Location
{{LocationName}} — {{LocationProse}}

## Time
{{DayOfWeek}}, {{DayPhase}} (hour {{Hour}})

## What changed
{{OutcomeDiff}}
```

Rules:

- The **placeholder inventory is fixed per agent**; document it in
  `docs/ai-authoring.md` and generate the table from engine constants so docs can't rot
  ([M8-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1074)).
- Never place a world/system directive that contradicts the engine contract — the override
  is content, not control.
- Keep prompts single-purpose and small: each agent owns one narrow contract.

---

## 5. Generality Proof: the Zero-Engine-Changes Rule ([M8-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1043))

When authoring a second world (different premise/tone), any feature markdown cannot express
goes on the **post-1.0 backlog**, not into a special-case engine hack. Re-skin existing
systems (stats, skills, economy, arcs) through rules and prose only.

---

## 6. Authoring Docs & the Cold-Reader Test

- `docs/world-format.md`: every folder kind and every frontmatter field (type, required,
  default, example), **generated where possible from engine schema** to prevent doc rot.
- `docs/ai-authoring.md`: character-sheet anatomy, prompt-override conventions, a
  placeholder table per agent, ≥ 2 annotated transcripts (grounded vs hallucinated), and a
  symptom → cause → fix troubleshooting matrix ([M8-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1065)).
- `docs/tutorial-your-first-world.md`: build a 2-location, 1-NPC micro world in 30 minutes
  via `world init` → `validate` → `pack` → install → play.
- Verify docs with a **cold read-through and a timer**, and a doc-completeness test that
  every validator-checked key appears in the reference.

### Annotated transcript format

```markdown
### Example: grounded (good)
> **Player:** ask Mia about the job
> **Mia:** "The café's hiring, if you can survive the 6 a.m. shift."

*Why it works: references the café (a known location) and Mia's own schedule.*

### Example: hallucinated (bad)
> **Mia:** "My sister runs the marina, I can get you a job there."

*Why it fails: "the marina" and "my sister" exist nowhere in the world; the name-lint flags both.*
```

---

## 7. Related Skills

- Loader/validator and placeholder validation: `markdown-ast-validator`.
- Injection defense and delimiters: `llm-redteam-evaluator`.
- Template engine and context packets: `ai-agent-orchestration`.
- Authoring workflow and ADRs: `adr-plan-workflow`.
