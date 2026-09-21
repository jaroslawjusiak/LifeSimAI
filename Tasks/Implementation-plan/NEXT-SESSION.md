# LifeSim Engine — Next Session

**Session:** 2026-09-21 · **Purpose:** resume point after a correctness-audit + remediation session.
If you are new to the repo, read [`AGENTS.md`](../../AGENTS.md) first.

The source of truth is [`correctness-tracker.md`](correctness-tracker.md): item table in
[§2](correctness-tracker.md#2-item-table-severity-ranked), execution order in
[§6](correctness-tracker.md#6-recommended-execution-order), open decisions in
[§5](correctness-tracker.md#5-open-decisions), refuted claims in
[§4](correctness-tracker.md#4-refuted--no-action-do-not-re-litigate). If it disagrees with this
note, the tracker wins. This file is a pointer, not a duplicate — do not copy D1–D14 evidence here.

## Start here

1. Tell the orchestrator: "read `Tasks/Implementation-plan/correctness-tracker.md` and continue
   from Batch 2." That routes D8 to `architect` first, then D7 to `developer`.
2. **Batch 2 — D7** (journal typed payloads / real replay) and **D8** (`TurnCorrelation.Mint()`
   uses `Guid.NewGuid()`). D8 needs an architect ruling and possibly an ADR before D7.
3. Then Batch 3 (V1–V3 evidence) → Batch 4 (D9–D11, V4) → Batch 5 (D13/D14 forward risks).

## Status

| Item | State |
| --- | --- |
| D1, D2, D3, D4, D5, D6, D12 | **Verified closed** (Batches 0 and 1 complete) |
| D7, D8, D9, D10, D11 | **Open** |
| V1, V2, V3, V4 | **Open** |
| D13, D14 | Forward-risk items (post-M1) |

Ruled and not to be re-litigated: **ADR-011** (boundary-based hour decay; `GameClockEventDispatcher`
owns the clock advance) and **ADR-012** (pass-out params live in `WorldRules`; forced sleep applied
after the advance). Both are recorded in [`plan.md`](plan.md).

Still owed (tracker [§5](correctness-tracker.md#5-open-decisions)): **D8** (`Guid.NewGuid`
determinism), **D9** (enforce vs defer location gating), **D13** (single-slot pass-out latch —
queue vs reject), **§5(e)** (M0 exit-gate wording), plus two **DoD trade-offs awaiting user
ratification** (§5(f) translator fail-fast; ADR-012 `WorldRules` public before any markdown test).

## Process and baseline

- An item may move to `Verified closed` only after an **independent** verifier (not the author)
  confirms it — route implementation to `developer`, then an independent `debugger`/`testsmith`
  check before `architect` closes it in the tracker
  (tracker [§1.2](correctness-tracker.md#12-status-legend)).
- Baseline as of session end: `dotnet build LifeSim.sln` → 0 warnings / 0 errors;
  `dotnet test LifeSim.sln` → 268 passed / 0 failed / 0 skipped, offline.
- Working tree was clean. HEAD is `672ee8c`; the last two commits (`655d629` code, `672ee8c`
  docs) are **local-only / unpushed** (`git status -sb` → `main...origin/main [ahead 2]`). Five
  earlier commits were pushed to `origin/main` from outside the agent session — the agent did not
  push. Confirm with the user before pushing the last two.
