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
| **D5** | High | Hour decay is **partition-dependent**: `hoursPassed = minutes / 60` per action, so two 30-minute actions yield **zero** decay for a full elapsed hour while one 60-minute action yields one tick; day detection is boundary-based, inconsistent with the chunk count. | `src/LifeSim.Core/Actions/ActionResolver.cs`:73–77; `src/LifeSim.Core/Time/GameClock.cs`:93–94; `src/LifeSim.Core/Time/GameClockEventDispatcher.cs`:25; encoded by `tests/LifeSim.Core.Tests/GameClockTests.cs`:119–127 | M1-01, M1-02 | **M1 exit gate (decay AC)** | developer (ruling: architect) | **Verified closed** — independent verification (debugger, 2026-09-21) supersedes the developer self-report; ruled boundary-based by `ADR-011`. Boundary arithmetic independently reproduced: `13:30+90 → BoundariesCrossed 2`, `23:59+1 → 1`, `14:30+60 → 1`, `14:30+30 → 1`, `14:30+29 → 0`; `week=168`, `multi-day=49`, `23:00+120=2` unchanged. **Partition independence proven and discriminating:** the 30+30 partition equals a single 60-min action in **both** `BoundariesCrossed` and total decay (energy 99 both ways); under the old `minutes/60` rule the partition gave `0` ticks, so the new tests **fail pre-fix**. `GameClockTests.cs`:119–127 (`1`→`2`), `:129–137` (`0`→`1`), `GameClockEventDispatcherTests.cs`:26–34 and `:48–59` changed with the reason stated inline per AGENTS rule 9. General gate: `dotnet build LifeSim.sln --no-incremental` → 0 Warning(s), 0 Error(s); `dotnet test LifeSim.sln` → 247 passed / 0 failed / 0 skipped (Core 167, AI 33, Console 45, World 1, Persistence 1), offline confirmed (`FakeChatClient`/stub handlers; no `Skip=`). **Residuals recorded under [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam).** See [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21). |
| **D6** | High | `StatConsequence.PassOut` is **reported but never enforced** — no forced sleep, no clock mutation, no mood penalty. Only `Starve` has a consequence. | `src/LifeSim.Core/Stats/StatConsequence.cs`:11–12; `src/LifeSim.Core/Stats/StatSet.cs`:76–101; `tests/LifeSim.Core.Tests/StatSetTests.cs`:44–56; `tests/LifeSim.Core.Tests/DemoWorld.cs`:17; plan.md:194, 199, 204 | M1-02 | **M1 exit gate** | developer | **Decided — awaiting implementation (ADR-012, 2026-09-21).** Pass-out parameters live in a new `WorldRules` record owned by `WorldState` (defaults: 6 h sleep, −10 mood); `StatDef`/`StatConsequence` are unchanged. The trigger only latches pending; forced sleep is applied **after** the action's advance completes, through the same dispatcher (never from an `HourPassed` handler — re-entrancy per ADR-011). See [§5(g)](#g-d6--pass-out-representation--application--resolved-2026-09-21). **Not fixed** until its closure proof passes. |
| **D7** | High | The journal is **lossy and not replayable**: `Payload` is `string?` (not the plan's typed payload records); `ActionResolved` drops `targetId`; failed turns and raw input are absent; the replay test passes only because the demo script needs no targets and has no randomness. | `src/LifeSim.Core/Journal/Journal.cs`:35–41; `src/LifeSim.Core/Actions/ActionResolver.cs`:79–83; `tests/LifeSim.Core.Tests/DemoWorldPlaythroughTests.cs`:42–56; plan.md:269, 274, 277 | M1-06 | **M1 exit gate**; M4-04; M6 | architect (payload shape) → developer | Open |
| **D8** | Decide | `TurnCorrelation.Mint()` uses `Guid.NewGuid()` inside `LifeSim.Core`, and the id is persisted into every journal entry; the state hash **excludes** the journal, which is why no test catches it. Needs a ruling: sanctioned diagnostics exception (with ADR) or a determinism fix. | `src/LifeSim.Core/Diagnostics/TurnCorrelation.cs`:19; `src/LifeSim.Core/Turns/GameLoop.cs`:169; `src/LifeSim.Core/Actions/ActionResolver.cs`:80; `src/LifeSim.Core/Entities/WorldState.cs`:88–89; `src/LifeSim.Core/Diagnostics/StateHasher.cs`:16; `AGENTS.md` §3 rule 4 | determinism principle; M1-06 | **M1 exit gate (journal determinism)** | architect (ruling/ADR) | Open |
| **D9** | Medium | Location graph is **data-only at runtime**: `Connections` / `RequiresFlag` / `AllowedActionIds` are never read by the resolver, so `Move` teleports to any existing location and any action runs anywhere. The gated-edge AC holds only as a data model. | `src/LifeSim.Core/Entities/Location.cs`:8, 46–49; `src/LifeSim.Core/Actions/ActionResolver.cs` (no read of `Connections` / `AllowedActionIds`); plan.md:217 | M1-03 | M3/M5 option layer if deferred | architect (decision) → developer | Open |
| **D10** | Medium | DemoWorld drifts from the plan: **7 actions with three `move_*` variants and no `talk`**, while the café advertises `talk` with no matching definition, and **no `Goal` type exists** anywhere. The integration fixture never exercises `Relationship`. | `tests/LifeSim.Core.Tests/DemoWorld.cs`:44–61, 37; no `Goal` type in `src/`; plan.md:287, 294 | M1-07 | **M1 exit gate** | developer | Open |
| **D11** | Medium | **No debt rule**: `Player.Money` is unclamped and `MoneyCost` is subtracted unconditionally; the plan requires negative money only when world rules enable debt. (A missing `MoneyGte` is *not* a gap — it was never an M1-04 requirement.) | `src/LifeSim.Core/Entities/Player.cs`:46; `src/LifeSim.Core/Actions/ActionResolver.cs`:68–71; plan.md:194; plan.md:231 (requirement list has no `MoneyGte`) | M1-02, M1-04 | **M1 exit gate** | developer | Open |
| **D12** | Medium | `GameClockEventDispatcher` has **zero call sites** — decay is invoked directly, so the plan's "decay applied on `HourPassed`" seam is a dead object. | `src/LifeSim.Core/Time/GameClockEventDispatcher.cs` (no references in `src/`); `src/LifeSim.Core/Actions/ActionResolver.cs`:76; plan.md:194, 186 | M1-01, M1-02 | none (correctness seam / cleanup) | developer (ruling: architect) | **Verified closed** — independent verification (debugger, 2026-09-21) supersedes the developer self-report; ruled "wire in, not delete" by `ADR-011`. Grep confirms **one** seam, **one** subscription, **one** call site: `WorldState` owns a private `GameClockEventDispatcher` and subscribes `HourPassed → Player.Stats.ApplyHourPassed` exactly once in the constructor; `ActionResolver.Apply` no longer loops `ApplyHourPassed`; `GameClockEventDispatcher.Advance` is the single advance owner (raises `HourPassed` once per boundary, `DayStarted` at most once per advance). **No double decay:** the direct `ApplyHourPassed` loop is gone, so decay fires only through the dispatch. General gate: `dotnet build LifeSim.sln --no-incremental` 0 Warning(s)/0 Error(s); `dotnet test LifeSim.sln` → 247/247. **Residual (public-API DoD trade-off) recorded under [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam).** See [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21). |
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
- **D9 still needs a decision** (enforce-now vs defer). D5's decision was ruled by `ADR-011`
  (see [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21)),
  and D5 is now independently verified and closed.
- **Batch 0 (D1–D4) is now independently verified** (debugger, 2026-09-21) and closed. The
  residuals that keep evidence honest are in the **V2** row above and in
  [§5(f)](#f-translator-failure-recovery-model--resolved-2026-09-21-not-open); none of them weakens
  the D1/D3/D4 conclusion, and D2's stated deviation is deliberate. V2 remains **Open**.
- **Batch 1 (D5, D12) is now independently verified** (debugger, 2026-09-21) and closed. **D6 is
  the batch's open remainder** (`Decided — awaiting implementation (ADR-012)`). The residuals that
  keep D5/D12 evidence honest — the `DemoWorld` balance signal (owner `worldsmith` / M1-07, tracked
  under D10), the transient-hunger comment imprecision, and the `ADR-011` public-API DoD trade-off —
  are recorded under [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam).

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

### (b) D5 + D12 — hour-decay semantics and the hourly-event owner — RESOLVED (2026-09-21)

- **Status: RESOLVED — binding ruling recorded as `ADR-011`** (appended after ADR-010 in
  `plan.md`'s ADR section). Decay is **boundary-based**, and `GameClockEventDispatcher` is **wired
  in** (not deleted) as the single owner of hour/day events. The fix has since landed and **D5 and
  D12 are verified closed** (independent `debugger` verification, 2026-09-21) — evidence and
  residuals in [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam). This section is the
  ruling; the D6 remainder of Batch 1 is still owed under `ADR-012` and must not be considered done
  until its closure proof passes.
- **Original forces (kept for provenance).** `GameClock.Advance` returned `minutes / 60` chunks
  (`GameClock.cs`:93) while day detection was boundary-based (`GameClock.cs`:94); two 30-minute
  actions gave zero decay for a full hour while one 60-minute action gave one tick; `hoursPassed = 0`
  for a one-minute advance across midnight was encoded as expected in `GameClockTests.cs`:129–137;
  `GameClockEventDispatcher` had zero call sites in `src/`, and `ActionResolver.Apply` called
  `StatSet.ApplyHourPassed` directly (`ActionResolver.cs`:122–126).
- **Decision.** An hour boundary is a clock hour mark (HH:00).
  1. **Decay is boundary-based.** `GameClock.Advance` returns
     `BoundariesCrossed = floor(newTotalMinutes / 60) − floor(TotalMinutes / 60)` (renamed from
     `HoursPassed`). Exactly one decay tick fires per boundary crossed. The count **telescopes** over
     any partition, so it is partition-independent and needs no carried state — a pure function of
     `(start clock, minutes)`.
  2. **`GameClockEventDispatcher` owns the advance.** It calls `GameClock.Advance`, raises
     `HourPassed` once per boundary crossed and `DayStarted` at most once per advance, and returns
     the advance result.
  3. **`WorldState` composes dispatcher → stats.** `WorldState` owns a **private**
     `GameClockEventDispatcher` and subscribes `Player.Stats.ApplyHourPassed` to `HourPassed`
     exactly once at construction. `WorldState.AdvanceClock` delegates to the dispatcher, and
     `ActionResolver.Apply` no longer loops `ApplyHourPassed`. Keeping the dispatcher private avoids
     a new public engine API. The flow is synchronous and subscription-ordered (single-threaded, no
     wall clock, no `Guid`), so it stays deterministic and offline-testable.
  4. **`DayStarted` is unchanged** — still a boolean raised at most once per advance
     (`plan.md`:195), because it is a notification, not accumulated state.
- **Rationale.** Identical elapsed time must produce identical state and identical state hashes
  whether it is spent as one action or many; otherwise the balance fuzzer, the journal replay and
  "decay per hour" all depend on action granularity rather than elapsed time. Boundary counting is
  the only partition-independent **stateless** rule (a remainder-carry counter would have to be
  persisted), and it makes decay consistent with the already boundary-based `dayStarted`.
- **Known accepted property.** Ticks land on HH:00, so a sub-hour advance that crosses a mark
  (e.g. 59 min from 13:30 to 14:29) ticks once, and any full-day advance ticks exactly 24 times.
  This phase-shift is the trade-off of a stateless rule and is accepted; over any whole number of
  hours the total is exact.
- **D6 re-entrancy constraint (recorded, not part of this ruling).** A consequence handler such as
  D6's pass-out forced-sleep must be applied **after** the action's advance completes — never by
  re-entering `WorldState.AdvanceClock` from inside an `HourPassed` handler.
- **Tests that change (AGENTS rule 9 reason stated inline).** `GameClockTests.cs`:119–127
  (13:30+90 → `1` becomes `BoundariesCrossed` `2`) and `GameClockTests.cs`:129–137 (23:59+1 → `0`
  becomes `1`); `GameClockEventDispatcherTests.cs`:26–34 (13:30+90 fires `2`, not `1`) and
  `:48–59` (23:59+1 fires `HourPassed` `1` and `DayStarted` `1`). The old values encoded the
  partition-dependent `minutes/60` chunk rule this ruling declares a defect. Unchanged:
  zero minutes (`0`), 13:30+29 (`0`), 23:00+120 (`2`), week rollover (`168`), multi-day (`49`),
  and every `DayStarted` test.
- **Batch 1 acceptance criteria (verbatim hand-off).**

  **D5 — boundary semantics**
  1. `GameClock.Advance` returns `(GameClock NewClock, int BoundariesCrossed, bool DayStarted)`
     with `BoundariesCrossed = newTotalMinutes / 60 − TotalMinutes / 60` (integer floor). The
     `HoursPassed` tuple name is gone; XML docs state the boundary contract.
  2. `WorldState.AdvanceClock` returns `(int BoundariesCrossed, bool DayStarted)` and delegates to
     the dispatcher; no other type computes the count independently.
  3. Partition-independence test (new, in `GameClockTests.cs`): two consecutive 30-minute advances
     14:30→15:00→15:30 sum to the same `BoundariesCrossed` as one 60-minute 14:30→15:30 advance
     (both `1`), and the sum over any two-chunk partition of a fixed interval equals the single
     advance's count.
  4. Updated assertions (reason inline, AGENTS rule 9):
     `Advance_MultiHour_WithinDay_DoesNotRollDay` → `BoundariesCrossed == 2`;
     `Advance_AcrossMidnight_RollsToNextDay` → `BoundariesCrossed == 1` (DayStarted still true).
  5. Unchanged assertions still pass: `Advance_ZeroMinutes` (`0`), `Advance_WithinHour` 13:30+29
     (`0`), `Advance_MultiHour_AcrossMidnight` 23:00+120 (`2`), `Advance_WeekRollover` (`168`),
     `Advance_MultipleDays` (`49`).
  6. Multi-day / long advances: a 24-hour advance crosses exactly `24` boundaries; a `30 × 24 h`
     advance crosses exactly `720`; `DayOfWeek` still derives from `DayIndex` (week rollover
     unchanged).

  **D12 — dispatcher wiring**
  7. `GameClockEventDispatcher.Advance(current, minutes)` is the only place that advances the clock
     for simulation: it raises `HourPassed` once per `BoundariesCrossed`, raises `DayStarted` at
     most once when the day index increased, and returns the advance result. Its XML doc describes
     boundary semantics.
  8. `WorldState` holds the dispatcher privately and subscribes `Player.Stats.ApplyHourPassed` to
     `HourPassed` exactly once at construction. `ActionResolver.Apply` no longer contains an
     `ApplyHourPassed` loop.
  9. Closure proof (new test): crossing exactly one hour boundary decays a `DecayPerHour` stat by
     exactly one tick; crossing two boundaries decays twice; an advance that crosses no boundary
     does not decay. A test at the resolver level proves the 30+30 partition decays the same total
     as the single 60-minute action.
  10. `GameClockEventDispatcherTests` updated: `Advance_NinetyMinutes…` asserts `2` (renamed to
      say so) and `Advance_OneMinuteAcrossMidnight…` asserts `HourPassed == 1`, `DayStarted == 1`
      (renamed to say so). `Advance_ReturnsTheAdvancedClock` updated for the new return shape.
      Unchanged: 1 h→`1`, 3 h→`3`, 48 h→`HourPassed` `48`/`DayStarted` `1`, within-day no
      `DayStarted`, day-index reporting, all-subscribers.
  11. No new public engine API on `WorldState` (dispatcher stays private/internal); the change is
      confined to `GameClock.cs`, `GameClockEventDispatcher.cs`, `WorldState.cs` and
      `ActionResolver.cs` plus their tests.
  12. General gate: `dotnet build LifeSim.sln -warnaserror` → 0 warnings/0 errors and
      `dotnet test LifeSim.sln` → all suites green, offline path included;
      `ActionResolverTests.Resolve_AdvancesClock_AndAppliesHourlyDecay` (starts on the hour,
      9:00+120→11:00) still expects `98`.

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

### (g) D6 — pass-out representation & application — RESOLVED (2026-09-21)

- **Status: RESOLVED — binding ruling recorded as `ADR-012`** (appended after ADR-011 in
  `plan.md`'s ADR section). This section is the ruling; the fix is owed by **Batch 1 (remainder)**
  and must not be considered done until its closure proof passes. It does **not** claim D6 is fixed.
- **Representation — `WorldRules`, not `StatDef`.** A BCL-only immutable record `WorldRules`
  (`LifeSim.Core.Rules`) carries the pass-out parameters: `ForcedSleepHours` (int, default **6**,
  must be ≥ 0), `PassOutMoodPenalty` (decimal, default **−10**, must be ≤ 0) and
  `PassOutMoodStatId` (string, default `"mood"`), plus a `Default` singleton. `WorldState` accepts an
  optional `WorldRules` (defaults to `WorldRules.Default`) and stores it privately. `StatDef` and
  `StatConsequence` are **unchanged** — `StatDef` keeps the consequence *selector*, `WorldRules`
  carries the *parameters* (as starvation already keeps `starvationDrainPerHour`/`starvationDrainTarget`
  off `StatDef`, on the `StatSet` constructor).
  **Why not `StatDef`:** the DoD forbids new engine API before a markdown-level test can drive it
  (`plan.md`:1408), and a per-stat payload is heterogeneous (the fields only mean anything for
  `PassOut`). **Why not `StatSet`:** it owns no clock, so it cannot express "N hours". **Why not
  defer:** M1-02's AC (`plan.md`:212) and the M1 exit gate require enforcement now.
- **Trigger semantics.** `WorldState` subscribes to the player's `StatSet.StatCritical`. A downward
  crossing with `Consequence == PassOut` **only latches a pending pass-out** (the stat id); the
  handler mutates nothing. This can be reached either from an action's energy cost (applied before
  the advance) or from hourly decay (inside the advance); both drain through the same mechanism.
- **Application semantics.** `ActionResolver.Apply` drains the pending pass-out **after** its own
  `AdvanceClock` and after the `ActionResolved`/`DayStarted` entries, via an internal `WorldState`
  method:
  1. advance the clock by exactly `ForcedSleepHours` hours through the **same dispatcher** — a
     multiple of 60 minutes always crosses exactly N boundaries, so exactly N `HourPassed` decay
     ticks fire (no hand-rolled decay loop);
  2. apply `PassOutMoodPenalty` to `PassOutMoodStatId` **exactly once** (clamped by that stat's
     bounds; skipped if the stat is absent — the M2 validator owns the content warning);
  3. journal the consequence (below).
  The triggering action's effects and costs are **not** rolled back or interrupted; pass-out adds
  time and a penalty, it does not cancel the action, and it does **not** restore energy. Forced
  sleep crossing midnight/day boundaries rolls `DayIndex` and journals `DayStarted` normally.
- **Once-per-crossing / re-arm.** The existing `Stat.IsCriticalArmed` semantics already make the
  crossing fire once and re-arm only after the value recovers above `CriticalAt`; the ruling adds
  only a re-entrancy guard: the pending latch is cleared **before** the forced-sleep advance, the
  sleep calls the dispatcher directly (never a nested `AdvanceClock`), and any crossing raised
  during the forced sleep is deferred to the next advance. Energy stuck at 0 never re-fires; after
  an action restores energy above 0 and it crosses down again, pass-out fires again.
- **Interaction with D5 / ADR-011 (no double decay).** The forced-sleep advance goes through the
  same `WorldState.AdvanceClock`/dispatcher path as the action advance; `ApplyHourPassed` is never
  called directly in the pass-out path. The two advances are separate dispatcher calls with
  telescoping boundary counts, so total decay equals the sum of the hourly ticks — the constraint
  recorded in §5(b) ("apply forced sleep **after** the advance completes, never from inside an
  `HourPassed` handler") is satisfied by the pending-latch design.
- **Determinism & replay.** Forced sleep is a pure function of `(clock, WorldRules)` — no RNG, no
  wall clock, no `Guid`. The journal records exactly one new
  `JournalEntryTypes.PassOut` entry per pass-out: `IsPinned = false`, `CorrelationId =
  TurnCorrelation.Current ?? string.Empty` (its determinism is governed by D8), `SimTime` = the
  clock at which the entity passed out, payload `"<statId>:<sleptHours>"` (e.g. `"energy:6"`).
  Replay order per triggering action: `ActionResolved` → (`DayStarted`) → `PassOut` →
  (`DayStarted` if the sleep rolled a day). Because D7 is still open, the payload stays a string
  now; when D7 lands typed records this becomes a `PassOutPayload(string StatId, int SleptHours)`.
  The journal entry is for observability/AI-context/debugging — action-driven replay
  (`Replay_ReapplyingJournal_ReproducesStateHash`) reproduces the same state hash whether or not the
  entry is replayed, because the consequence is re-derived from the re-applied action.
- **DoD tension, recorded plainly.** `WorldRules` is a new **public** Core type added before any
  markdown-level test exists, so it is a scoped exception to
  [`AGENTS.md` §7](../../AGENTS.md) / `plan.md`:1408. It is kept to one record and three fields, it
  is the exact materialization target M2-06 already scopes (`plan.md`:429), and **M2-06 owes the
  markdown-level test that populates it from `rules/` content**. Extending `StatDef` would be a
  strictly larger and less content-aligned violation. This is the smallest surface that satisfies
  M1-02; it is recorded as debt, not waved through.
- **Scope.** Player only in M1 — NPCs are not turn-simulated yet.
- **Batch-1-remainder acceptance criteria (verbatim hand-off).**

  **Representation**
  1. `WorldRules` exists in `LifeSim.Core` (BCL-only; namespace `LifeSim.Core.Rules`) with
     `ForcedSleepHours` (int, default 6, validated ≥ 0), `PassOutMoodPenalty` (decimal, default −10,
     validated ≤ 0), `PassOutMoodStatId` (string, default `"mood"`) and `Default`. `StatDef` and
     `StatConsequence` are unchanged; no new fields on either.
  2. `WorldState` takes an optional `WorldRules` (defaulting to `WorldRules.Default`) and stores it
     privately — no new public member on `WorldState` beyond the optional constructor parameter.

  **Trigger**
  3. The `StatCritical` handler only latches pending. A test crosses energy with `PassOut` and
     asserts `world.Clock` and every stat value are unchanged until the drain.
  4. Pass-out triggers from either the action's energy cost or hourly decay and drains identically.

  **Application**
  5. Draining advances by `ForcedSleepHours` hours via the dispatcher; a test proves exactly N
     `HourPassed`/decay ticks fire and no manual `ApplyHourPassed` loop exists.
  6. The mood penalty is applied exactly once, clamped, to `PassOutMoodStatId`; absent target stat is
     a no-op (asserted).
  7. The triggering action's effects/costs are not rolled back; the consequence does not restore
     energy (asserted).

  **Re-entrancy / once-per-crossing**
  8. No recursion and no double-apply: a crossing raised during the forced-sleep advance is deferred;
     the clock advances by exactly N.
  9. `StatSetTests.PassOut_Consequence_IsReportedOnCrossing` (reporting) is unchanged; a new
     enforcement test proves pass-out fires **once**, does not re-fire while energy stays 0, and
     fires again after energy recovers above `CriticalAt` and crosses down again.

  **Journal & determinism**
  10. Exactly one `JournalEntryTypes.PassOut` entry per pass-out with `SimTime` = pass-out clock and
      payload `"<statId>:<sleptHours>"`; a `DayStarted` entry when the sleep rolled a day; order
      `ActionResolved` → (`DayStarted`) → `PassOut` → (`DayStarted`).
  11. Two identical runs yield identical `StateHasher.Compute(world)`; the pass-out path contains no
      RNG/wall-clock/`Guid`; `Replay_ReapplyingJournal_ReproducesStateHash` still passes with a
      playthrough that triggers pass-out.

  **D5/ADR-011 interaction**
  12. A test proves total decay over an action plus its pass-out equals the sum of boundary ticks
      (no double count) and that `ApplyHourPassed` is not called directly in the pass-out path.

  **Gate**
  13. Pass-out is engine-only (no AI); offline suites stay green. `dotnet build LifeSim.sln
      -warnaserror` → 0 warnings/0 errors; `dotnet test LifeSim.sln` → all suites green.

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
passed"** — **D5/D12 have since closed** (Batch 1, independent verification 2026-09-21), and
**D6–D11 and V1–V4 remain open**.

### Batch 1 — simulation correctness (clock/decay seam)

**Items: D5, D6, D12.** Exactly one open decision governs this batch and it is now **RULED
(2026-09-21)** — see [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21)
and `ADR-011`: decay is **boundary-based** (`BoundariesCrossed` per HH:00 boundary) and
`GameClockEventDispatcher` is **wired in** as the single owner of the advance; `WorldState`
subscribes `HourPassed → Player.Stats.ApplyHourPassed`. Implement against the §5(b) acceptance
criteria, enforce the pass-out consequence (D6 — parameters in `WorldRules`, apply forced sleep
**after** the advance completes, never from inside an `HourPassed` handler; ruled by `ADR-012`,
see [§5(g)](#g-d6--pass-out-representation--application--resolved-2026-09-21)), and do not treat
D5/D12 as fixed until their closure proofs pass.

**Status: D5 + D12 DONE — independently verified by `debugger` on 2026-09-21** (supersedes the
developer self-report). Both are **Verified closed**. **D6 remains `Decided — awaiting
implementation (ADR-012)` — this slice does not close it** (no `WorldRules`/`PassOut`
implementation exists yet). The batch therefore closes its two clock/decay-seam items and leaves
the pass-out remainder open.

**Evidence (verbatim, `debugger` 2026-09-21) — supersedes the author's self-report:**
- `dotnet --version` → `10.0.401`.
- `dotnet build LifeSim.sln` and `dotnet build LifeSim.sln --no-incremental` → **0 Warning(s),
  0 Error(s)**.
- `dotnet test LifeSim.sln` → **247 passed / 0 failed / 0 skipped**; Core **167**, AI 33,
  Console 45, World 1, Persistence 1. Offline confirmed (stub handlers / `FakeChatClient`);
  no `Skip=`. Core went 150→167 = **+17 cases; no test removed** (230→247).
- Boundary arithmetic independently reproduced: `13:30+90 → BoundariesCrossed 2`, `23:59+1 → 1`,
  `14:30+60 → 1`, `14:30+30 → 1`, `14:30+29 → 0`; `week=168`, `multi-day=49`, `23:00+120=2`
  unchanged.
- **Partition independence proven and a genuine discriminator:** the 30+30 partition equals a
  single 60-min action in both `BoundariesCrossed` **and** total decay (energy 99 both ways);
  under the old `minutes/60` rule the partition gave 0 ticks, so the new tests **fail pre-fix**.
- **No double decay:** `ActionResolver.Apply` no longer loops `ApplyHourPassed`; `WorldState` owns
  a private dispatcher and subscribes `HourPassed → Player.Stats.ApplyHourPassed` exactly once in
  the constructor; grep shows one seam, one subscription, one call site.
- The changed integration assertion was audited: `DemoWorldPlaythroughTests` `health 100→85` is a
  legitimate deterministic consequence of correct decay (20 boundaries/iteration vs 19; hunger
  transiently hits 0 for 3 ticks; pre-existing `Starve` drain 3×5=15) — **not** a masked regression
  and **not** double decay.
- Scope: `src`/`tests` changed only in `Time/GameClock.cs`, `Time/GameClockEventDispatcher.cs`,
  `Entities/WorldState.cs`, `Actions/ActionResolver.cs` plus `GameClockTests.cs`,
  `GameClockEventDispatcherTests.cs`, `ActionResolverTests.cs`, `DemoWorldPlaythroughTests.cs`.
  No `WorldRules`/`PassOut` implementation exists (D6 unimplemented). No test removed;
  230→247 = +17 cases.

**Residuals recorded honestly (none of them re-opens D5/D12):**
1. **Balance signal (not a test defect) — owner `worldsmith` / [M1-07](../plan.md), tracked under
   D10.** The demo world's 7-day script now transiently starves (hunger touches 0 for 3 hourly
   ticks; ends health 85, hunger 24). An 8-day run would drive health toward 0. `DemoWorld` numbers
   were tuned against the buggy decay. **Do not action here beyond recording** — D10 already tracks
   the `DemoWorld` drift.
2. **Comment imprecision (cosmetic).** The inline rule-9 reason says the loop "drives hunger to 0";
   it is *transient* (final hunger 24). Note only; no behaviour implication.
3. **Public-API DoD trade-off (acknowledged, ruling-level).** `GameClock.Advance`'s tuple element
   was renamed (`HoursPassed` → `BoundariesCrossed`) and `GameClockEventDispatcher.Advance` now
   returns a tuple — both are rulings of `ADR-011`, and **no markdown-level test exists yet** that
   drives them from content. This is the same acknowledged class of trade-off recorded for D6 in
   [§5(g)](#g-d6--pass-out-representation--application--resolved-2026-09-21); recorded as debt, not
   waved through. The next content-driven engine API test is owed by M2.

**Effect on the M1 exit gate:** D5 and D12's contribution is satisfied, but the gate **stays "not
passed"** — **D6 remains open**, and D7–D11 and V1–V4 remain open.

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
| **D5** | **Met — independently verified (debugger, 2026-09-21).** A test asserts two consecutive 30-minute actions across an hour boundary produce the **same** decay as one 60-minute action, and that `BoundariesCrossed` from `Advance` is partition-independent; both hold (30+30 == 60 in count and total decay, energy 99 both ways; old `minutes/60` rule gave 0, so the test fails pre-fix). `GameClockTests.cs`:119–127 and `GameClockEventDispatcherTests` were updated with the stated reason; `13:30+90 → 2`, `23:59+1 → 1`, `14:30+30 → 1`, `14:30+29 → 0`, `week=168`, `multi-day=49`, `23:00+120=2` all reproduce; `dotnet build LifeSim.sln --no-incremental` 0/0; `dotnet test LifeSim.sln` 247/247. Ruled boundary-based by `ADR-011`; see [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21) for the full acceptance criteria and [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam) for the recorded residuals. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~GameClock"` then `dotnet test LifeSim.sln` |
| **D6** | Pass-out is enforced: a `StatCritical` crossing with `PassOut` only **latches pending** (no mutation in the handler); after the action's advance completes, the clock advances by the `WorldRules` forced-sleep hours through the dispatcher, the mood penalty is applied **exactly once**, and it re-arms after recovery. The journal contains a single `PassOut` entry (`"<statId>:<sleptHours>"`). Ruled by `ADR-012`; see [§5(g)](#g-d6--pass-out-representation--application--resolved-2026-09-21) for the full acceptance criteria. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~PassOut"` then `dotnet test LifeSim.sln` |
| **D7** | A test replays a scripted session containing a parameterised `Move` (with `targetId`) **from the journal alone** and reproduces `StateHasher.Compute(original)`. The journal type carries typed payloads (not `string?`); failed turns and raw input are present and replay/idempotent. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~Replay"` then `dotnet test LifeSim.sln` |
| **D8** | A ruling is recorded (ADR appended after `plan.md`:1376 if it is an exception). If fixed: a test asserts two identical seeded turns produce identical correlation ids and a byte-stable journal. | ADR recorded in `plan.md`'s ADR section **and** `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~TurnCorrelation"` |
| **D9** | A test asserts `Move` to a target absent from the current location's `Connections` is refused with a readable reason, and a `RequiresFlag` edge is refused until the flag is set. Deferred `AllowedActionIds`/open-hours is explicitly noted. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~ActionResolver"` then `dotnet test LifeSim.sln` |
| **D10** | `tests/LifeSim.Core.Tests/DemoWorld.cs` matches `plan.md`:287 — exactly 3 locations, 3 NPCs, 6 actions **including `talk`**, 1 goal; every café `AllowedActionIds` entry resolves; the playthrough test asserts a `Relationship` change/milestone actually occurs. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~DemoWorld"` then `dotnet test LifeSim.sln` |
| **D11** | A test asserts that with debt disabled an action whose `MoneyCost` would drive money negative is refused with a readable reason and mutates nothing; with debt enabled by world rules it succeeds to negative. | `dotnet test tests/LifeSim.Core.Tests/LifeSim.Core.Tests.csproj --filter "FullyQualifiedName~ActionResolver"` then `dotnet test LifeSim.sln` |
| **D12** | **(a) chosen by `ADR-011` — Met, independently verified (debugger, 2026-09-21).** `GameClockEventDispatcher` is wired into the clock advance (it owns the advance, raises `HourPassed` per boundary), `WorldState` subscribes `HourPassed → Player.Stats.ApplyHourPassed` exactly once, and decay fires only through the dispatch (no direct `ApplyHourPassed` loop remains); grep shows one seam, one subscription, one call site. Option (b) (delete) is rejected. Build 0 Warning(s)/0 Error(s); `dotnet test LifeSim.sln` 247/247. See [§5(b)](#b-d5--d12--hour-decay-semantics-and-the-hourly-event-owner--resolved-2026-09-21) and [§6 Batch 1](#batch-1--simulation-correctness-clockdecay-seam) (public-API DoD trade-off residual). | `dotnet build LifeSim.sln -warnaserror` (proves no dangling reference) then `dotnet test LifeSim.sln` |
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
