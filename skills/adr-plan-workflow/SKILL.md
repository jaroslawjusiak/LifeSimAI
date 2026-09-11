---
name: adr-plan-workflow
description: |
  Use when starting, implementing, or completing a LifeSim Engine story or milestone,
  or when an architectural decision must be recorded. Guides reading the implementation
  plan, dependency order, story implementation protocol, ticking plan.md checkboxes,
  writing ADR-00x records, and updating the changelog/DoD gate.
---

# Implementation Plan & ADR Workflow in LifeSim Engine

The plan (`Tasks/Implementation-plan/plan.md`) is the single source of truth for scope,
ordering and acceptance criteria. This skill is the protocol for moving a story from
`[ ]` to `[x]` without drifting from it.

---

## 1. Story Lifecycle

```
Locate story → check _Depends on:_ → read Design notes & AC → write failing tests
→ implement → build/test green → verify DoD → tick subtasks → record ADR (if needed)
→ update CHANGELOG → commit referencing the story id
```

### Step 1 — Locate and scope

Search the plan for the story id (e.g. `M3-04`) and read **all four** blocks:

1. The one-line "As a …, I want …" statement.
2. **Design notes & edge cases** — the real spec; the largest source of hidden requirements.
3. **Acceptance criteria** — the test list. Each bullet becomes at least one test.
4. **Subtasks** — the checklist you tick when done.

Confirm every id in `_Depends on:_` is already `[x]` before writing code.

### Step 2 — Tests first

Turn each acceptance criterion into a failing test before implementation. If a criterion
cannot be tested (e.g. "renders actionably"), define the observable proxy and note it.

### Step 3 — Implement by layer

Respect the dependency direction
([M0-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L61)):
domain + rules in `LifeSim.Core`; I/O in the owning layer. See
`solution-architecture-guardrails`.

### Step 4 — Gate on the Definition of Done

```powershell
dotnet build LifeSim.sln -warnaserror
dotnet test  LifeSim.sln
```

Zero warnings, all suites green, **offline path included** (fake client / AI disabled).

### Step 5 — Tick subtasks in the plan

Subtask lines go from `- [ ]` to `- [x]` only after the work is verified. Never pre-tick.
Only edit the plan's checkbox lines; leave design notes intact.

---

## 2. Architecture Decision Records

When you make a decision that is expensive to reverse, record it next to the existing
ADRs (ADR-001…ADR-008) using the established shape:

```markdown
### ADR-009 — <short imperative title>

- **Context:** What forced the decision. Constraints, forces, prior art.
- **Decision:** What we will do, stated concretely.
- **Rejected alternatives:** Each option and the specific reason it lost.
```

Rules:

- One decision per ADR; never edit a decided ADR retroactively — supersede it (`ADR-0NN — … (supersedes ADR-00M)`).
- Match the tone of existing records: alternatives are **rejected with a reason**, not merely listed.
- Add the ADR only when the choice is architectural (a new dependency, a format, an
  interaction model, a boundary) — not for routine refactors.

Existing decisions worth reading before proposing alternatives:
[ADR-001 single process](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1322),
[ADR-002 Spectre.Console](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1328),
[ADR-003 markdown world format](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1334),
[ADR-004 in-process agents](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1340),
[ADR-005 Jan host](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1346),
[ADR-006 AI proposes/engine disposes](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1352),
[ADR-007 JSON snapshot saves](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1358),
[ADR-008 .lifeworld packages](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1364).

---

## 3. Milestone Exit Gates

Do not start the next milestone until the current exit gate passes. Gates are explicit in
the plan (e.g. [M0](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L53),
[M1](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L168),
[M4](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L581)). Treat a gate as
a release checklist, not a suggestion.

Scope discipline ([R-12](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1314)):
if a feature is not in a story, it does not get built — add it to the post-1.0 backlog in
the plan instead.

---

## 4. Commit & Change Log Conventions

- Commit subject: `M3-04: action menu with costs and disabled reasons`.
- One story per commit where practical; reference the story id in the body.
- Update `CHANGELOG.md` for user-visible changes; note prompt-template version bumps
  (required by [M9-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1125)).

---

## 5. Related Skills

- Layer boundaries and DoD: `solution-architecture-guardrails`.
- Test conventions: `dotnet-testing-standards`.
- Agent/prompt work: `ai-agent-orchestration`, `world-prompt-authoring`.
