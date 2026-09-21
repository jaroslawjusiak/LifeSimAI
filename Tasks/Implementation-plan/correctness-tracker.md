# LifeSim Engine — Correctness & Plan-Confidence Tracker

Tracking document for the verified gaps between the **ticked M0/M1 subtasks** in
[`plan.md`](plan.md) and the **proven acceptance criteria**. This file is append-only in spirit:
items are opened once, then moved through the status legend below as evidence lands. It does not
replace `plan.md`; it records *confidence*, not scope.

> **Scope discipline.** This tracker is **docs only**. It does not modify `plan.md`, any
> `src/` or `tests/` file, or any `*.csproj`. Creating or closing a code item here is a
> recommendation to the owning agent — the item is not "done" until the closure proof below
> exists and passes.

---

## 1. Provenance

| Field | Value |
| --- | --- |
| Trigger | An independent reviewer critiqued how the code corresponds to `Tasks/Implementation-plan/plan.md` |
| Verification | Every claim below was read first-hand against the sources and tests listed; a hostile second opinion plus a full-solution build/test run were used |
| Verified on | 2026-09-21 |
| SDK | .NET SDK `10.0.401` (target `net10.0`) |
| Build baseline | `dotnet build LifeSim.sln -warnaserror` → **Build succeeded, 0 Warning(s), 0 Error(s)** |
| Test baseline | `dotnet test LifeSim.sln --no-build` → **219 passed, 0 failed, 0 skipped** (Core 139, Console 45, AI 33, World 1, Persistence 1) |
| Refuted claims | See [§4](#4-refuted--no-action-do-not-re-litigate) — four reviewer claims are false and must never become work items |

**Path convention.** The original brief abbreviated some paths; every path here is the full
repository-relative path (e.g. `GameLoop.cs` is
`src/LifeSim.Core/Turns/GameLoop.cs`). Line references were spot-checked and are correct.

### 1.1 "Subtask code exists" is not "acceptance criteria proven"

The reviewer graded the repository *"M1 substantially implemented / promising"*. That is a
false negative: several **ticked** M1 subtasks overstate completion because the acceptance
criteria are either unmet or unproven. This tracker exists to make that distinction explicit.

| Story | Ticked subtask (from `plan.md`) | Reality | Item |
| --- | --- | --- | --- |
| M1-02 | `[x] Consequence hooks (pass-out, starvation) + tests` (plan.md:204) | Starvation enforced; **pass-out is only reported, never enforced** | D6 |
| M1-05 | all three subtasks (plan.md:259–261) | Turn pipeline runs work before its own transition; transitions and failures are journaled wrongly | D1–D4 |
| M1-06 | all three subtasks (plan.md:277–279) | Journal is lossy (`string?` payload, `targetId` dropped, failed turns absent); replay is a special case | D7, D8 |
| M1-07 | `[x] DemoWorld builder with entities + actions + one goal` (plan.md:294) | No `Goal` type exists; no `talk` action; three `move_*` variants instead | D10 |
| M1-04 | `[x] Evaluator tests for every kind (incl. atomicity & clamp interplay)` (plan.md:243) | Atomicity AC has **no** mid-apply-failure proof | V1 |

Rule for this document: **an item closes only when its closure proof exists and passes** — never
because the code "looks done" or a plan checkbox is ticked.

### 1.2 Status legend

| Status | Meaning |
| --- | --- |
| **Open** | Verified defect/gap, no fix started. |
| **In progress** | Owner has begun the fix. |
| **Fixed (awaiting verification)** | Code/tests changed; closure proof not yet independently confirmed. |
| **Verified closed** | Closure proof exists, passes, and was independently confirmed (not self-reported by the author). |
| **Blocked (decision)** | Cannot proceed until an Open Decision in [§5](#5-open-decisions) is ruled on. |
| **Refuted / no-action** | Claim disproved; recorded so it is never re-litigated. |

---

## 2. Item table (severity-ranked)

Severity: **Blocker** > **High** > **Medium** > **Low**; **Decide** = an architectural ruling is
the deliverable; **Verify-only** = work is claimed done but unproven. Every evidence cell is
**VERIFIED** by direct source read on 2026-09-21.

| ID | Severity | Finding (one line) | Evidence (VERIFIED) | Affects | Blocks | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **D1** | High | Every stage's **work runs before its own state transition**; `Validating` is never actually occupied (validation *and* application both happen inside `Resolve`). | `src/LifeSim.Core/Turns/GameLoop.cs`:94→102, 108→103/116, 120→127, 132→140, 145→153; `src/LifeSim.Core/Actions/ActionResolver.cs`:108 | M1-05 | **M1 exit gate (hard)** | developer | **Verified closed** — independent verification (debugger, 2026-09-21) supersedes the developer self-report: `dotnet build LifeSim.sln --no-incremental` → 0 Warning(s), 0 Error(s); `dotnet test LifeSim.sln` → 230 passed / 0 failed / 0 skipped (Core 150, World 1, Persistence 1, AI 33, Console 45). Airtight D1 proof `RunTurn_EntersEachDelegateStage_WithStateSetToThatStage` captures `loop.State` synchronously inside each delegate, so no stage's work runs before its own entry transition. |
| **D2** | High | `TryTransition` return values are **ignored** at six call sites — a refused transition is silently swallowed and the pipeline continues, so the journal can desync from execution. | `src/LifeSim.Core/Turns/GameLoop.cs`:102, 103, 116, 127, 140, 153, 154 | M1-05 | **M1 exit gate** | developer | **Verified closed — deviation stated.** Independent verification (debugger, 2026-09-21): the six ignored `TryTransition` results are gone (uniform `TryEnter` at every site); discriminator `RunTurn_RefusedTransition_ReturnsTypedFailureAndStopsPipeline` fails pre-fix / passes post-fix; `dotnet build LifeSim.sln --no-incremental` 0/0; `dotnet test LifeSim.sln` 230/230. **Deviation:** the §7 clause "drives each refused/illegal transition" is only *partly* met — only the first transition (`Idle→InputReceived`) is forced; the later `TryEnter` sites are unexercised. This remainder is **not claimed as covered** and is tracked under V2. |
| **D3** | High | Failure journaling is wrong: **(a)** a failed stage is still journaled as entered; **(b)** the outer catch hardcodes `Applying` for any escaping exception; **(c)** `Failure` returns *before* the journal `Append`, so failed actions, raw input and the translated command are never journaled. | `src/LifeSim.Core/Turns/GameLoop.cs`:75–79, 94–100, 166–173; `src/LifeSim.Core/Actions/ActionResolver.cs`:53–56, 79 | M1-05, M1-06 | **M1 exit gate**; M6 | developer | **Verified closed** — independent (debugger, 2026-09-21): `dotnet build LifeSim.sln --no-incremental` 0/0; `dotnet test LifeSim.sln` 230/230. Discriminator `RunTurn_EscapingException_RecordsRealStageNotApplying` proves the escaping-exception path records the **real** stage (not hardcoded `Applying`); failure / raw-input / translated-command journaling asserted by the GameLoop failure tests. |
| **D4** | High | Translator failure falls back to **executing raw input as an action id**, and a test enshrines it (`IsSuccess == true`). | `src/LifeSim.Core/Turns/GameLoop.cs`:96–100; `src/LifeSim.Core/Turns/TranslatedCommand.cs`:4; `tests/LifeSim.Core.Tests/GameLoopTests.cs`:55–64 | M1-05, M5-03 | **M1 exit gate** | developer | **Verified closed** — independent (debugger, 2026-09-21): `dotnet build LifeSim.sln --no-incremental` 0/0; `dotnet test LifeSim.sln` 230/230. Discriminator `RunTurn_TranslatorFailure_DoesNotExecuteRawInputAsAction` (raw input is never resolved as an action). `RunTurn_RecoversTranslatorFailure_UsesFallback` assertion changed **and strengthened** (`IsSuccess` now `false`; the old `true` enshrined the D4 defect), justified inline per AGENTS rule 9. Recovery semantics resolved — see [§5(f)](#f-translator-failure-recovery-model--resolved-2026-09-21-not-open). |
| **D5** | High | Hour decay is **partition-dependent**: `hoursPassed = minutes / 60` per action, so two 30-minute actions yield **zero** decay for a full elapsed hour while one 60-minute action yields one tick; day detection is boundary-based, inconsistent with the chunk count. | `src/LifeSim.Core/Actions/ActionResolver.cs`:73–77; `src/LifeSim.Core/Time/GameClock.cs`:93–94; `src/LifeSim.Core/Time/GameClockEventDispatcher.cs`:25; encoded by `tests/LifeSim.Core.Tests/GameClockTests.cs`:119–127 | M1-01, M1-02 | **M1 exit gate (decay AC)** | architect (decision) → developer | Open |
| **D6** | High | `StatConsequence.PassOut` is **reported but never enforced** — no forced sleep, no clock mutation, no mood penalty. Only `Starve` has a consequence. | `src/LifeSim.Core/Stats/StatConsequence.cs`:11–12; `src/LifeSim.Core/Stats/StatSet.cs`:76–101; `tests/LifeSim.Core.Tests/StatSetTests.cs`:44–56; `tests/LifeSim.Core.Tests/DemoWorld.cs`:17; plan.md:194, 199, 204 | M1-02 | **M1 exit gate** | developer | Open |
| **D7** | High | The journal is **lossy and not replayable**: `Payload` is `string?` (not the plan's typed payload records); `ActionResolved` drops `targetId`; failed turns and raw input are absent; the replay test passes only because the demo script needs no targets and has no randomness. | `src/LifeSim.Core/Journal/Journal.cs`:35–41; `src/LifeSim.Core/Actions/ActionResolver.cs`:79–83; `tests/LifeSim.Core.Tests/DemoWorldPlaythroughTests.cs`:42–56; plan.md:269, 274, 277 | M1-06 | **M1 exit gate**; M4-04; M6 | architect (payload shape) → developer | Open |
| **D8** | Decide | `TurnCorrelation.Mint()` uses `Guid.NewGuid()` inside `LifeSim.Core`, and the id is persisted into every journal entry; the state hash **excludes** the journal, which is why no test catches it. Needs a ruling: sanctioned diagnostics exception (with ADR) or a determinism fix. | `src/LifeSim.Core/Diagnostics/TurnCorrelation.cs`:19; `src/LifeSim.Core/Turns/GameLoop.cs`:169; `src/LifeSim.Core/Actions/ActionResolver.cs`:80; `src/LifeSim.Core/Entities/WorldState.cs`:88–89; `src/LifeSim.Core/Diagnostics/StateHasher.cs`:16; `AGENTS.md` §3 rule 4 | determinism principle; M1-06 | **M1 exit gate (journal determinism)** | architect (ruling/ADR) | Open |
| **D9** | Medium | Location graph is **data-only at runtime**: `Connections` / `RequiresFlag` / `AllowedActionIds` are never read by the resolver, so `Move` teleports to any existing location and any action runs anywhere. The gated-edge AC holds only as a data model. | `src/LifeSim.Core/Entities/Location.cs`:8, 46–49; `src/LifeSim.Core/Actions/ActionResolver.cs` (no read of `Connections` / `AllowedActionIds`); plan.md:217 | M1-03 | M3/M5 option layer if deferred | architect (decision) → developer | Open |
| **D10** | Medium | DemoWorld drifts from the plan: **7 actions with three `move_*` variants and no `talk`**, while the café advertises `talk` with no matching definition, and **no `Goal` type exists** anywhere. The integration fixture never exercises `Relationship`. | `tests/LifeSim.Core.Tests/DemoWorld.cs`:44–61, 37; no `Goal` type in `src/`; plan.md:287, 294 | M1-07 | **M1 exit gate** | developer | Open |
| **D11** | Medium | **No debt rule**: `Player.Money` is unclamped and `MoneyCost` is subtracted unconditionally; the plan requires negative money only when world rules enable debt. (A missing `MoneyGte` is *not* a gap — it was never an M1-04 requirement.) | `src/LifeSim.Core/Entities/Player.cs`:46; `src/LifeSim.Core/Actions/ActionResolver.cs`:68–71; plan.md:194; plan.md:231 (requirement list has no `MoneyGte`) | M1-02, M1-04 | **M1 exit gate** | developer | Open |
| **D12** | Medium | `GameClockEventDispatcher` has **zero call sites** — decay is invoked directly, so the plan's "decay applied on `HourPassed`" seam is a dead object. | `src/LifeSim.Core/Time/GameClockEventDispatcher.cs` (no references in `src/`); `src/LifeSim.Core/Actions/ActionResolver.cs`:76; plan.md:194, 186 | M1-01, M1-02 | none (correctness seam / cleanup) | architect (wire vs delete) → developer | Open |
| **V1** | Verify-only | M1-04 atomicity AC is **unproven**: no mid-apply-error test; costs, clock and journal sit outside any transaction; the XML doc claims a successful resolve "is atomic". | plan.md:235; `src/LifeSim.Core/Actions/ActionResolver.cs`:8–12, 73–83, 221–309; `tests/LifeSim.Core.Tests/ActionResolverTests.cs`:283–310 (only failed-precondition + invalid-reference) | M1-04 | M1 exit gate (AC unproven) | testsmith | Open |
| **V2** | Verify-only | Transition/recovery AC **partly met**: only 2 transitions and 3 of 5 stage-exception paths are tested; the resolver-throw (`Applying`) and `WorldTick` catches have zero tests; there is no `Recovery` state. | plan.md:254; `tests/LifeSim.Core.Tests/GameLoopTests.cs`:24–90; `src/LifeSim.Core/Turns/TurnState.cs`:7–17 | M1-05 | M1 exit gate (AC unproven) | testsmith | Open — **Batch 0 residuals now tracked here** (independent verification, 2026-09-21): (1) D2's refused-transition matrix is only partly covered — only `Idle→InputReceived` is forced, later `TryEnter` sites unexercised; (2) 2 of the 9 new GameLoop tests are **non-discriminating** — `RunTurn_JournalsStageTransitionsInExecutionOrder` and `Resolve_SuccessfulAction_DoesNotAppendActionFailedEntry` pass against pre-fix code, so they are guards, not fix evidence; (3) the `StageTransition.Seq < StageFailed.Seq` assertions in the Validating/Applying tests are meaningful but **not airtight** (pre-fix they fail for a different reason — no `StageFailed` entry existed); (4) small follow-up (**owner: testsmith**): rename `RunTurn_RecoversTranslatorFailure_UsesFallback`, which now asserts the opposite of its name. |
| **V3** | Verify-only | "Clock cannot be mutated outside the resolver" has **no test**. `ArchitectureTests` is two assembly-level checks only; `WorldState.AdvanceClock` is `internal` and **no `InternalsVisibleTo` exists in `src/`**. | plan.md:181; `tests/LifeSim.Core.Tests/ArchitectureTests.cs`:9–51; `src/LifeSim.Core/Entities/WorldState.cs`:80 | M1-01 | M1 exit gate (AC unproven) | testsmith | Open |
| **V4** | Verify-only | M0 exit gate is **not formally signed off**; the plan conflates "subtask ticked" with "gate passed". The "second machine or fresh profile" runbook clause is not provable from the repo. **Caveat:** `docs/model-acceptance.md` *is* a real recorded result and `docs/local-llm-setup.md` *does* carry the hardware→model matrix, so M0-05/M0-06 are substantially met. | plan.md:53–55, 135, 152; `docs/model-acceptance.md`:5–16; `docs/local-llm-setup.md`:40–43, 167 | M0 | M1 start (process gate) | architect + scribe | Open |

Notes on the table:

- **D1 is worse than first reported.** `Validating` is doubly fictional: both validation *and*
  application happen inside `ActionResolver.Resolve` (`ActionResolver.cs`:108), which is called
  *before* either `TryTransition` at `GameLoop.cs`:103/116. The state machine and the journal
  therefore describe a pipeline that did not execute.
- **D8 is a "Decide", not a defect with a pre-agreed fix.** It is recorded here because it is a
  structural/determinism question; see [§5(a)](#5-open-decisions).
- **D5 and D9 also need decisions.** D5 changes test semantics; D9 decides enforce-now vs defer.
- **Batch 0 (D1–D4) is now independently verified** (debugger, 2026-09-21) and closed. The
  residuals that keep evidence honest are in the **V2** row above and in
  [§5(f)](#f-translator-failure-recovery-model--resolved-2026-09-21-not-open); none of them weakens
  the D1/D3/D4 conclusion, and D2's stated deviation is deliberate. V2 remains **Open**.

---

## 3. Dependency map

```text
D1 ─┬─ D2 ─┬─ D3 ── D4          Batch 0 (turn pipeline)
    │      │
    │      └──────────── D7 ── (typed payload + replay)
    │
D5 ─┴─ D6 ── D12                 Batch 1 (clock/decay seam)
D8 ──────────────────── D7       Batch 2 (journal determinism)

V1 ── V2 ── V3                   Batch 3 (evidence)
D9 ── D10 ── D11 ── V4           Batch 4 (remaining decisions/data)
```

- **D7 inherits D3/D4** (what gets journaled on failure) **and D8** (the correlation id written
  into entries). Fix the timeline before reshaping the journal, or the payload schema will encode
  a corrupted order.
- **Update (2026-09-21):** D3 has now landed failure journaling (raw input, translated command,
  `ActionFailed`) and D4 has landed the translator-failure no-execute rule. **D7 still owns typed
  payload records and general replay**, so the D3/D4 → D7 arrow **stands unchanged**.
- **D1/D2 share one fix region** (`GameLoop.cs`); D3/D4 share a second (`failure handling`).
  Sequence them as one batch to avoid churn.
- **D2 ↔ V2**: the expanded transition tests in V2 should be written against the *corrected*
  transition semantics, not the current ones.

---

## 4. Refuted / no-action (do not re-litigate)

These reviewer claims are **false**. They are recorded so no future session turns them into work
items. No code changes are warranted for any of them.

| Ref | Reviewer claim | Rebuttal (VERIFIED) | Action |
| --- | --- | --- | --- |
| **R-REF1** | "ADR-001…ADR-010 not present." | They exist at `plan.md`:1322–1380. The project convention keeps ADRs inside `plan.md` (skills link to `plan.md` anchors — e.g. `.opencode/skills/adr-plan-workflow/SKILL.md`:86–93). The reviewer read a file listing, not the plan. | **None** |
| **R-REF2** | "CPM lock-file workflow unconfirmed." | Ten `packages.lock.json` files exist (one per project); `Directory.Build.props`:10 sets `RestorePackagesWithLockFile=true`; `dotnet restore LifeSim.sln --locked-mode` exits 0. | **None** |
| **R-REF3** | "build/test gate could not be verified." | SDK `10.0.401` present; `dotnet build LifeSim.sln -warnaserror` → Build succeeded, 0 Warning(s), 0 Error(s); `dotnet test LifeSim.sln` → 219/219 passed, 0 failed. The reviewer's failure was an environment artifact of their review. | **None** |
| **R-REF4** | "Relationship milestones unverified / not downward-tested." | `src/LifeSim.Core/Entities/Relationship.cs`:22, 46–55 handles ±25/±50/±75 with re-arming; `tests/LifeSim.Core.Tests/RelationshipTests.cs`:22–81 covers upward, negative and re-crossing. | **None** |

**Two small reviewer inaccuracies** (also no action, recorded for the same reason):

| Ref | Inaccuracy | Correction (VERIFIED) |
| --- | --- | --- |
| **R-ACC1** | Implies a `GameClock` comment "defers per-world phase config". | The actual comment is `src/LifeSim.Core/Time/DayPhase.cs`:6 — per-world phase configuration "arrives with world rules". A design statement, not a defect. |
| **R-ACC2** | Queries the absence of debt handling. | The *debt gap is valid* and is tracked as **D11**. But `MoneyGte` was **never** a plan requirement (`plan.md`:231) — do not open a work item for a missing `MoneyGte`. |

**Reviewer positives (acknowledged context, not action items):** architecture alignment (one-way
dependency direction, `Core` BCL-only), test organisation (per-layer suites with offline
fixtures), and separation of concerns. These are accurate and should be preserved through the
fixes above, not re-worked.

---

## 5. Open decisions

Each of these must be **ruled on and recorded** before the dependent items can close. Decide with
the option set below, not by drifting into an implementation.

### (a) D8 — `Guid.NewGuid()` determinism ruling (structural; needs an ADR if it is an exception)

- **Forces:** `TurnCorrelation.Mint()` uses `Guid.NewGuid()` (`TurnCorrelation.cs`:19) and the id
  is written into journal entries (`GameLoop.cs`:169, `ActionResolver.cs`:80). `AGENTS.md` §3
  rule 4 forbids `Guid.NewGuid()` in domain logic. The state hash deliberately excludes the
  journal (`WorldState.cs`:88–89, `StateHasher.cs`:16), so nothing detects it.
- **Option 1 — sanctioned diagnostics exception.** Correlation ids are metadata, not domain
  state; exclude them explicitly from determinism and document that in the journal schema and an
  ADR appended after `ADR-010` (`plan.md`:1376). *Cost:* an ADR; a documented carve-out from rule 4.
- **Option 2 — deterministic derivation.** Derive the per-turn id from a seeded counter/seed
  threaded through the turn so replay reproduces it byte-for-byte. *Cost:* a Core API change and
  a determinism test; removes the rule-4 violation without an exception.
- **Recommendation:** Option 2 if replay must reproduce journal entries (the plan calls the
  journal the single source of truth — `plan.md`:269, 274). The turn loop is single-threaded, so
  deterministic ordering is straightforward. If Option 1 is chosen, the ADR is **mandatory** and
  must state the cost. **No ADR is created by this document** — the next sequential number by the
  existing convention (`plan.md`:1322–1380) would be `ADR-011`, appended to the ADR section.

### (b) D5 — decay semantics: chunk-vs-boundary

- **Forces:** the existing `GameClock.Advance` returns `minutes / 60` chunks
  (`GameClock.cs`:93), while day detection is boundary-based (`GameClock.cs`:94). Two 30-minute
  actions give zero decay for a full hour; one 60-minute action gives one tick. `hoursPassed = 0`
  for a one-minute advance across midnight is *encoded as expected* in
  `GameClockTests.cs`:130–137.
- **Option 1 — boundary-based whole hours.** Count hour boundaries crossed from
  `TotalMinutes / 60`, so decay is partition-independent. *Cost:* `GameClockTests.cs`:119–127 and
  `GameClockEventDispatcherTests` must change; `AGENTS.md` rule 9 requires that change to be
  explicitly justified.
- **Option 2 — keep chunk semantics, document them.** Cheaper and test-stable, but the "decay
  per hour" world rule (`plan.md`:194) is then not what the number says, and balance drifts.
- **Recommendation:** Option 1, recorded as a decision (ADR if it defines a semantic contract).
  Whatever is chosen, `ActionResolver`, `GameClock` and `GameClockEventDispatcher` must agree.

### (c) D9 — enforce location gating now vs defer

- **Forces:** M1-03's AC says the graph "supports one-way connections and **gated edges** (requires
  key/flag)" (`plan.md`:217). The data exists (`Location.cs`:8, 46–49) but the resolver never
  reads it, so the AC holds only as a model.
- **Option 1 — enforce edge gating now.** Reject `Move` to a target not present in the current
  location's `Connections`, and reject a `RequiresFlag` edge until the flag is set. This is pure
  `Core` rules and satisfies the AC. *Cost:* small resolver change + tests.
- **Option 2 — defer `AllowedActionIds`/open-hours enforcement.** Those are UI/option-layer
  concerns and are **not** in the M1-03 AC text; deferring them with an explicit note is
  acceptable.
- **Recommendation:** Option 1 for edge/flag gating (the AC); Option 2 for `AllowedActionIds`
  and open-hours, deferred to M3/M5 with a recorded note. Close D9 when edge gating is enforced
  and tested; leave the deferred part explicit.

### (d) `plan.md` checkbox corrections

**DECIDED & APPLIED (2026-09-21).** The user explicitly approved correcting the over-ticked
checkboxes, and the correction has been applied to `plan.md` as a separate, approved edit (this
tracker still does not edit `plan.md` itself). Several ticked subtasks overstated completion (see
[§1.1](#11-subtask-code-exists-is-not-acceptance-criteria-proven)). The mechanism, stated in a
legend at the top of the M1 section (`plan.md:169–175`, post-edit), is:

- **re-opened (`[ ]`)** where the named deliverable genuinely does not exist;
- **partial (`[x]` kept, short marker appended)** where the code exists but the acceptance
  criterion is unmet or unproven.

No story was renumbered, no section reordered, no acceptance criterion reworded, and no scope
changed. Every corrected line cites a tracker item id.

| Tracker item | Story · subtask | `plan.md` line (pre → post) | Before → After |
| --- | --- | --- | --- |
| **D12** | M1-01 · `Internal event dispatcher publishing HourPassed/DayStarted` | 186 → 199 | `[x]` → `[x] … _partial (D12: dispatcher has zero call sites)._` |
| **D6** | M1-02 · `Consequence hooks (pass-out, starvation) + tests` | 204 → 217 | `[x]` → `[ ] … _re-opened (D6: pass-out not enforced)._` |
| **V1** | M1-04 · `Evaluator tests for every kind (incl. atomicity & clamp interplay)` | 243 → 256 | `[x]` → `[x] … _partial (V1: atomicity proof missing)._` |
| **D1–D4** | M1-05 · `GameLoop with stage delegates + TurnResult/TurnError types` | 259 → 272 | `[x]` → `[x] … _partial (D1–D4: timeline/transition defects)._` |
| **V2** | M1-05 · `Recovery routing (no unhandled exceptions escape the loop)` | 260 → 273 | `[x]` → `[x] … _partial (V2: no Recovery state)._` |
| **D7** | M1-06 · `Journal store (append-only) + typed payload records` | 277 → 290 | `[x]` → `[ ] … _re-opened (D7: payload is a string, not typed records)._` |
| **D7** | M1-06 · `Replay tool used by tests (state-hash comparison)` | 279 → 292 | `[x]` → `[x] … _partial (D7: replay is a special case, not general)._` |
| **D10** | M1-07 · `DemoWorld builder with entities + actions + one goal` | 294 → 307 | `[x]` → `[ ] … _re-opened (D10: no Goal type, no talk action)._` |
| **D1–D4** | M1-05 · `GameLoop with stage delegates + TurnResult/TurnError types` (marker **removal**, 2026-09-21) | 272 → 272 | `[x] … _partial (D1–D4: timeline/transition defects)._` → plain `[x]` — D1–D4 **Verified closed** (independent, debugger 2026-09-21); the residual test-matrix gap is tracked under V2. The `Recovery routing … _partial (V2: no Recovery state)._` marker on the next line is **left in place**. |

The checkbox pass inserted 3 lines just below the M0 gate and a further 10 lines between the M1
intro and M1-01. Pre-edit `plan.md` references used elsewhere in this tracker therefore shift by
**+3** below the M0 gate and by **+13** below the M1 legend; the pre → post column above is exact.

Prose-only additions beside the checkboxes (the acceptance criteria text is unchanged):

- a three-line **legend** at the top of the M1 section (`plan.md:169–175`) defining the two markers;
- a one-line **M1 exit-gate status** note (`plan.md:182–184`) recording the gate as **not passed**
  while D1–D12 / V1–V4 remain open;
- a one-line **M0 exit-gate status** note (`plan.md:57–58`) recording the gate as **met except the
  "second machine or fresh user profile" clause** (V4 / [§5(e)](#e-is-the-m0-exit-gate-formally-not-passed)).

**Left ticked deliberately** (no overstatement found): M1-05 `Headless harness used by integration
tests` (plan.md:261 pre-edit) — genuinely done; M1-06 `Query API + pinned/rolling classification`
(plan.md:278 pre-edit) — `EventJournal.Last/ByType/SinceDay/Pinned/Rolling` exist and are
exercised, and the "respected by the context builder later" clause is an M4 deliverable.

**Status: decided and applied — no further user decision is required for the checkbox pass.**
Closing the underlying items remains governed by [§7](#7-per-item-closure-criteria).

### (e) Is the M0 exit gate formally "not passed"?

- **Forces:** M0's gate requires "Jan runbook verified by a second machine or fresh user profile"
  (`plan.md`:55), echoed by M0-05's AC (`plan.md`:135) — neither is provable from the repo.
  However `docs/model-acceptance.md`:5–16 is a real dated probe result and
  `docs/local-llm-setup.md`:40–43 carries the hardware→model matrix, so M0-05/M0-06 are
  substantially met (see V4).
- **Recommendation:** record M0 as **"passed except the second-machine clause"** rather than
  "passed". Either perform and archive the second-machine/fresh-profile run, or waive the clause
  explicitly with a stated reason. Keep **V4 Open** until that ruling is written down.

### (f) Translator-failure recovery model — RESOLVED (2026-09-21), not open

- **Status: RESOLVED.** This is recorded as a ruling, not an open decision — do not re-litigate it.
  It is *not* a new ADR; the recovery-model ADR is still owed under V2 (below).
- **What was decided (D4).** A **configured** translator that throws now **fails the turn** with a
  typed `TurnError(Translating)` and mutates nothing: the pipeline stops and raw input is **never**
  executed as an action id. The offline / `Translator == null` path still completes the full
  pipeline, and the `WorldTick`, `Narrator` and `Options` stages still recover-and-continue.
- **ADR-006 rationale.** The rule is "AI proposes, engine disposes": the model's output passes the
  AIGate and only the engine mutates state. A translator that threw produced **no command**, so
  there is nothing for the engine to dispose — no candidate delta to schema-validate, whitelist,
  precondition-check or clamp. Executing raw text as an action id would be the engine inventing a
  state change from unvalidated input, which is precisely what ADR-006 forbids. Failing the turn
  typed and side-effect-free is the ADR-006-consistent recovery.
- **DoD tension, recorded deliberately.** The DoD line "kill the stage, the turn still completes"
  is **narrowed for the translator stage only**: a configured translator that throws fails the turn
  (typed `TurnError(Translating)`, no mutation) instead of executing raw input. This is a scoped
  exception, not a general weakening — every stage still returns a typed result, the loop still
  never throws across the UI boundary, and the offline path still completes end-to-end.
- **Evidence.** `RunTurn_TranslatorFailure_DoesNotExecuteRawInputAsAction` (airtight discriminator);
  `RunTurn_RecoversTranslatorFailure_UsesFallback` with its assertion strengthened (`IsSuccess`
  now `false`, justified inline per AGENTS rule 9). See the D4 row in [§2](#2-item-table-severity-ranked).
- **Still owed (V2 / §5).** The **recovery-model ADR** — typed `TurnError` + `Idle` with no
  `Recovery` state — is **still required** under V2. This entry resolves the translator's *behaviour*;
  it does not substitute for that ADR. V2 remains **Open**.

---

## 6. Recommended execution order

Do the batches in order. Cross-batch dependencies are called out; within a batch, land D1–D2
together and D3–D4 together to avoid rewriting the same region twice.

### Batch 0 — FIRST SLICE: fix the turn timeline (unblocks the M1 exit gate) · **DONE — independently verified 2026-09-21**

**Items: D1, D2, D3, D4.** This is the smallest set that makes the M1 exit gate reachable.
Rationale: all four live in the turn pipeline (`GameLoop.cs` plus failure journaling in
`ActionResolver.cs`), and replay, AI context packets and saves all inherit the corrupted
timeline. Fixing anything downstream first encodes the corruption and forces re-work.

**Exit condition:** every stage is *entered* (journaled transition) before its work runs; no
`TryTransition` result is ignored; a failure journals the path that actually executed; a failed
translator routes to a defined deterministic recovery/no-op and never executes raw text as an
action id.

**Status: DONE — independently verified by `debugger` on 2026-09-21** (supersedes the developer
self-report). D1, D3, D4 are **Verified closed**; D2 is **Verified closed with a stated deviation**
(its full refused-transition matrix is a V2 residual). The exit condition is met: stages are entered
before their work runs, no `TryTransition` result is ignored, failures journal the path that
actually executed, and the failed-translator path is a typed `TurnError(Translating)` no-op that
never executes raw text — see [§5(f)](#f-translator-failure-recovery-model--resolved-2026-09-21-not-open)
for the narrowed "recovery/no-op" reading.

**Evidence (verbatim, `debugger` 2026-09-21):**
- `dotnet --version` → `10.0.401`.
- `dotnet build LifeSim.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)** —
  not a stale-artifact green.
- `dotnet test LifeSim.sln` → **230 passed / 0 failed / 0 skipped**; per-suite Core 150, World 1,
  Persistence 1, AI 33, Console 45. Core went 139→150 (+11 = 9 new `GameLoopTests` + 2 new
  `ActionResolverTests`).
- Offline confirmed: `FakeChatClient` + stub `HttpMessageHandler`s; `127.0.0.1:1337` appears only
  as config-string literals; whole run ≈ 1.3 s.
- Scope confined to `src/LifeSim.Core/Turns/GameLoop.cs`,
  `src/LifeSim.Core/Actions/ActionResolver.cs`, `src/LifeSim.Core/Journal/Journal.cs`,
  `tests/LifeSim.Core.Tests/GameLoopTests.cs`, `tests/LifeSim.Core.Tests/ActionResolverTests.cs`
  (plus the pre-existing approved `plan.md` checkbox edit). **No new public engine API**
  (`Validate`/`Apply`/`ActionValidation` are `internal`; `Resolve` remains the public facade).
- No test removed or weakened; one assertion changed **and strengthened**
  (`RunTurn_RecoversTranslatorFailure_UsesFallback`), justified inline per AGENTS rule 9.
- Airtight proofs: `RunTurn_EntersEachDelegateStage_WithStateSetToThatStage` (D1);
  `RunTurn_TranslatorFailure_DoesNotExecuteRawInputAsAction` (D4);
  `RunTurn_EscapingException_RecordsRealStageNotApplying` (D3);
  `RunTurn_RefusedTransition_ReturnsTypedFailureAndStopsPipeline` (D2).

**Effect on the M1 exit gate:** Batch 0's contribution is satisfied, but the gate **stays "not
passed"** — D5–D12 and V1–V4 remain open.

### Batch 1 — simulation correctness (clock/decay seam)

**Items: D5, D6, D12.** Decide D5 semantics first (Open Decision (b)), then make
`ActionResolver`, `GameClock` and `GameClockEventDispatcher` agree; enforce the pass-out
consequence; resolve the dead dispatcher (wire or delete) as part of the same seam.

**Depends on:** none, but touches the same tests as Batch 0's timeline work — avoid running them
concurrently in separate branches.

### Batch 2 — journal shape and determinism

**Items: D7, D8.** Rule on D8 (Open Decision (a)) before finalising the journal payload schema,
then introduce typed payloads, journal raw input and failed turns, and make replay reconstruct a
parameterised session.

**Depends on:** Batch 0 (what gets journaled on failure) and the D8 ruling.

### Batch 3 — close the evidence gaps

**Items: V1, V2, V3.** Add the missing proofs against the corrected semantics from Batches 0–2.
Cheap, high signal, and required by the DoD "all suites green" gate.

**Depends on:** Batch 0 (V2 transition tests) and Batch 1 (V1 clock/cost interaction).

### Batch 4 — remaining decisions and data gaps

**Items: D9, D10, D11, V4.** Rule on D9 (Open Decision (c)), align `DemoWorld` with `plan.md`:287,
add the debt gate, and record the M0 gate status (Open Decision (e)).

**Depends on:** no hard blockers; can proceed in parallel with Batch 3 once Batches 0–1 have landed.

---

## 7. Per-item closure criteria

General gate (applies to every item): `dotnet build LifeSim.sln -warnaserror`
(**0 Warning(s), 0 Error(s)**) **and** `dotnet test LifeSim.sln` (**all suites green, offline
path included**). An item is not closed until its own proof passes **and** the general gate is
green.

Targeted commands below use the existing `tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj`
project. Test names in *italics* are **to be added**; the file named is the existing file to
extend.

| ID | Closure proof (observable assertion) | Command that proves it |
| --- | --- | --- |
| **D1** | A test in `tests/LifeSim.Core.Tests/GameLoopTests.cs` asserts the journaled `StageTransition` sequence equals the executed order and that **no stage's work runs before its own entry transition** (spy on translator/`ActionResolver` observes the loop state). | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~GameLoop"` then `dotnet test LifeSim.sln` |
| **D2** | A test drives each refused/illegal transition and asserts the loop returns a typed failure (`TurnError`) instead of continuing, and that **no** `ActionResolved` entry is journaled for a refused turn. | same as D1 |
| **D3** | Tests prove: a failed stage is **not** journaled as entered; an escaping exception records a `TurnError` with the **real** stage (not hardcoded `Applying`); a failed `Resolve` appends a journal entry; raw input and the translated command are journaled. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~GameLoop"` then `dotnet test LifeSim.sln` |
| **D4** | A test *`RunTurn_TranslatorFailure_DoesNotExecuteRawInputAsAction`*: with a throwing translator, raw text is not resolved as an action; the turn routes to a deterministic recovery/no-op. The existing `RunTurn_RecoversTranslatorFailure_UsesFallback` (`GameLoopTests.cs`:55–64) is updated, with the reason stated (AGENTS rule 9). | same as D1 |
| **D5** | A test asserts two consecutive 30-minute actions across an hour boundary produce the **same** decay as one 60-minute action, and that the hour count from `Advance` is partition-independent. `GameClockTests.cs`:119–127 and `GameClockEventDispatcherTests` are updated with the stated reason. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~GameClock"` then `dotnet test LifeSim.sln` |
| **D6** | A test asserts energy reaching 0 with `PassOut` advances the clock by the forced-sleep hours and applies a mood penalty, **exactly once**, re-arming after recovery. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~StatSet"` then `dotnet test LifeSim.sln` |
| **D7** | A test replays a scripted session containing a parameterised `Move` (with `targetId`) **from the journal alone** and reproduces `StateHasher.Compute(original)`. The journal type carries typed payloads (not `string?`); failed turns and raw input are present and replay/idempotent. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~Replay"` then `dotnet test LifeSim.sln` |
| **D8** | A ruling is recorded (ADR appended after `plan.md`:1376 if it is an exception). If fixed: a test asserts two identical seeded turns produce identical correlation ids and a byte-stable journal. | ADR recorded in `plan.md`'s ADR section **and** `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~TurnCorrelation"` |
| **D9** | A test asserts `Move` to a target absent from the current location's `Connections` is refused with a readable reason, and a `RequiresFlag` edge is refused until the flag is set. Deferred `AllowedActionIds`/open-hours is explicitly noted. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~ActionResolver"` then `dotnet test LifeSim.sln` |
| **D10** | `tests/LifeSim.Core.Tests/DemoWorld.cs` matches `plan.md`:287 — exactly 3 locations, 3 NPCs, 6 actions **including `talk`**, 1 goal; every café `AllowedActionIds` entry resolves; the playthrough test asserts a `Relationship` change/milestone actually occurs. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~DemoWorld"` then `dotnet test LifeSim.sln` |
| **D11** | A test asserts that with debt disabled an action whose `MoneyCost` would drive money negative is refused with a readable reason and mutates nothing; with debt enabled by world rules it succeeds to negative. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~ActionResolver"` then `dotnet test LifeSim.sln` |
| **D12** | Either (a) `GameClockEventDispatcher` is wired into the clock advance and a test proves `HourPassed` drives decay, **or** (b) it is deleted and the M1-01 subtask/ADR note is updated. The choice is recorded. | `dotnet build LifeSim.sln -warnaserror` (proves no dangling reference) then `dotnet test LifeSim.sln` |
| **V1** | A test injects a **mid-apply** failure and proves no partial application (staged apply or rollback), or the XML doc at `ActionResolver.cs`:8–12 is corrected to match the actual guarantee. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~Atomic"` then `dotnet test LifeSim.sln` |
| **V2** | Tests cover the resolver-throw (`Applying`) and `WorldTick` exception paths, the full legal/illegal transition matrix, and **all five** stage-exception paths. Either add a `Recovery` state or record in an ADR that typed `TurnError` + `Idle` is the recovery model. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~GameLoop"` then `dotnet test LifeSim.sln` |
| **V3** | An architecture/encapsulation test proves `WorldState.Clock` cannot be mutated outside the resolver (e.g. reflection scan, or `InternalsVisibleTo` + an internal-only test). | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~Architecture"` then `dotnet test LifeSim.sln` |
| **V4** | The M0 gate status and its evidence are recorded (build/test + probe archive + hardware matrix), and the second-machine/fresh-profile clause is either performed and archived or explicitly waived with a reason. | `git status --short` shows the recorded doc/plan change; `dotnet test LifeSim.sln` remains green |

---

## 8. Maintenance

- **Open an item** only with a repo-relative `file:line` or a `plan.md` line as evidence.
- **Move to Verified closed** only with the closure proof above passing and independently
  confirmed — never on the strength of a plan checkbox.
- **Never re-open a Refuted claim** ([§4](#4-refuted--no-action-do-not-re-litigate)) without new
  evidence.
- **Keep `plan.md` authoritative.** If this tracker and `plan.md` disagree about scope, `plan.md`
  wins; record the disagreement under [§5(d)](#d-planmd-checkbox-corrections).
