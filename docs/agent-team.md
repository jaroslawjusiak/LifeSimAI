# The LifeSim Agent Team

Nine project-level [opencode](https://opencode.ai/docs/agents/) agents living in
`.opencode/agents/`. They are committed to the repository, so every clone gets the same team.

Shared project rules live in [`AGENTS.md`](../AGENTS.md) at the repo root — every agent inherits
them. Deep domain knowledge lives in [`skills/`](../skills/) and is referenced by name from the
agent prompts rather than duplicated.

---

## Quick start

```
opencode                      # starts in the primary agent
@orchestrator implement M2-03 # route a task through the orchestrator
@rubber-duck score this plan  # invoke a subagent directly
@debugger the build is red    # go straight to the specialist
```

Press **Tab** to cycle primary agents. Subagents are invoked with `@name`, or automatically by
the orchestrator via the Task tool.

---

## The team

| Agent | File | Mode | Temp | Can edit | Owns |
| --- | --- | --- | --- | --- | --- |
| **Orchestrator** | `orchestrator.md` | `primary` | 0.2 | nothing | Classify → route → verify DoD → report |
| **Architect** | `architect.md` | `subagent` | 0.3 | `Tasks/`, `docs/`, `*.md` | Layer boundaries, ADRs, API placement, plan structure |
| **Rubber Duck** | `rubber-duck.md` | `subagent` | 0.4 | nothing | Scored second opinion (1–10), weak spots, alternatives |
| **Developer** | `developer.md` | `subagent` | 0.3 | everything | Implementing plan stories, test-first |
| **Debugger** | `debugger.md` | `subagent` | 0.2 | everything | Reproduce → triage → minimal root-cause fix |
| **Testsmith** | `testsmith.md` | `subagent` | 0.3 | `tests/`, `Tasks/` | Test design, coverage gate, determinism, flakiness |
| **Sentinel** | `sentinel.md` | `subagent` | 0.4 | `tests/`, `docs/`, `Tasks/` | AIGate, injection defence, red-team fixtures, integrity |
| **Worldsmith** | `worldsmith.md` | `subagent` | 0.7 | `*.md`, `*.yml` | World content, prompt overrides, balance |
| **Scribe** | `scribe.md` | `subagent` | 0.4 | `*.md`, `docs/` | README, docs, CHANGELOG, release notes |

Temperature reflects the job: `debugger` and `orchestrator` are near-deterministic, `worldsmith`
is deliberately the most creative agent on the team.

**No agent pins a `model`.** Every one inherits whatever model the session is using. To pin one,
add `model: provider/model-id` to its frontmatter — see
[the opencode docs](https://opencode.ai/docs/agents/#model).

---

## How work flows

```
                        ┌──────────────┐
              user ───▶ │ orchestrator │  classify · route · verify · report
                        └──────┬───────┘
                               │
        ┌──────────┬───────────┼───────────┬──────────┬──────────┐
        ▼          ▼           ▼           ▼          ▼          ▼
   architect  rubber-duck  developer   debugger   testsmith   sentinel
   (decide)    (score)     (build)      (fix)      (prove)    (attack)
        │          │           │           │          │          │
        └──────────┴─────┬─────┴───────────┴──────────┴──────────┘
                         ▼
                   worldsmith · scribe
                   (content · documentation)
```

Typical chains:

| Request | Chain |
| --- | --- |
| "Implement M2-04" | `architect` (if the shape is unclear) → `developer` → `testsmith` → `scribe` |
| "The build is broken" | `debugger` → `testsmith` if the suite itself is at fault |
| "Should we do X?" | `rubber-duck` (score) → `architect` (binding decision + ADR) |
| "Add a new AI stage" | `architect` → `developer` → `sentinel` (gate it) → `testsmith` |
| "Author a second world" | `worldsmith` → `sentinel` (injection/whitelist) → `scribe` (authoring docs) |
| "Is this plan any good?" | `rubber-duck` → `architect` for plan edits |

---

## The two enforcement ideas worth knowing

**1. Permissions encode the process, not just safety.**

The orchestrator has `edit: deny` — it physically cannot do the implementation it is supposed to
delegate. The architect can write ADRs and plans but not `src/`. The testsmith can write tests but
not the code under test, so an untestable seam becomes a *reported finding* instead of a quiet
workaround. The sentinel finds holes and writes the failing test that proves them, then hands the
fix over. Where an agent's job is to decide rather than to do, the permission block says so.

Bash permissions follow the same logic: read-only agents get `cat`/`grep`/`find` and no writes;
`debugger` gets the full `dotnet` toolchain; nobody gets a free `git push`, `git reset --hard`,
`rm`, or a pinned-version change.

**2. Every agent ends with a report contract.**

Each prompt closes with a fixed report format. The point is evidence: real commands, real output,
real gaps. `developer` and `debugger` must paste build/test results; `scribe` must state which
commands it actually ran; `rubber-duck` must cite files and ADRs for every claim; `sentinel` must
attach a failing fixture to every finding. "Should be fine" is not an acceptable output anywhere
on this team.

---

## Design notes

**Rubber Duck is a reviewer, not an approver.** Its prompt forbids rubber-stamping: it must
produce a scored rubric, at least one concrete weak spot, and at least one real alternative with
its own trade-offs. The 1–10 scale is anchored — a proposal that merely satisfies its ACs and
matches the ADRs is a 7, and a 10 is treated as suspicious.

**Sentinel's threat model is scoped to this app.** Local, single-player, offline-capable, no
auth, no network surface. The prompt lists thirteen threats that can actually happen here
(prompt injection via world prose, hallucinated entity ids, delta-envelope violations, save
tampering, zip-slip in `.lifeworld`, Spectre markup injection…) and explicitly names the
non-threats, so it does not waste turns on enterprise theatre. It also requires *exploitability*
to be stated separately from severity — "the player edits their own save" is Low unless it
corrupts state.

**Worldsmith runs at 0.7 and can only touch content.** Prose quality needs headroom; the ability
to edit `src/` does not. Its prompt makes "a second world ships with zero engine changes" the
test it must satisfy, and tells it to report an engine assumption rather than work around one.

**Scribe exists because the DoD has a docs item that nobody owned.** It also inherits a real gap:
`CHANGELOG.md` does not exist yet even though the DoD and `skills/adr-plan-workflow` require one.
Its prompt tells it to create the file on first need and *not* to invent back-history.

**Subagents have `task: deny`.** Only the orchestrator can spawn work. That keeps the chain
visible and prevents a delegate loop where `developer` summons `testsmith` summons `debugger`
and nobody can tell you what happened. If you want a specialist to hand off directly, flip that
one agent's `task` permission and add an explicit allow-list.

---

## Tuning

| To… | Do this |
| --- | --- |
| Pin a model for one agent | add `model: provider/model-id` to its frontmatter |
| Make an agent less/more creative | adjust `temperature` (0.0–0.2 focused, 0.6+ creative) |
| Cap cost on a chatty agent | add `steps: <n>` to its frontmatter |
| Let a subagent delegate | change `task: deny` to an allow-list, e.g. `task: { "*": deny, debugger: allow }` |
| Hide an internal agent from `@` autocomplete | add `hidden: true` |
| Retire an agent | add `disable: true` (keeps the file for reference) |
| Change the agent's colour in the UI | `color:` accepts a hex value or a theme colour |
| Add a new specialist | drop `<name>.md` in `.opencode/agents/` — the filename becomes the agent name. Then add it to the orchestrator's routing table **and** its `permission.task` allow-list, or the orchestrator will be unable to call it. |

Note that `.opencode/agents/` is deliberately **not** gitignored — these are project-level agents
and are meant to be shared with everyone who clones the repo.
