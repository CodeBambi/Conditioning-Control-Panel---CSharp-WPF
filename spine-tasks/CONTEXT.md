# Conditioning-Control-Panel — Context

**Last Updated:** 2026-07-19
**Status:** Active
**Next Task ID:** SP-011

---

## Current State

Greenfield Avalonia port (second attempt), zero product code under `client/` yet. Execution engine: pi-spine (owner-decided 2026-07-18, replacing `@mjasnikovs/pi-task` — see `client/docs/task-board.md` gate history). Product queue authority is `client/docs/task-board.md`; this file tracks spine execution phases only. Workers obey `docs/constitution.md`.

### Phase 0 — Engine pilot and gates

| Task | Summary | Status | Deps |
|------|---------|--------|------|
| SP-001-avalonia-template-pilot | Throwaway Avalonia 12 template spike proving the spine pipeline end-to-end (real dotnet contract, review, gate, integrate) | Done (integrated `9a24a78a`) | None |

**Continuous-run authorization (owner decision 2026-07-18, chat):** the port runs autonomously until no claimable board work remains. SP-001 ratified; Phase 1 decomposition approved. Per-task owner checkpoints are replaced by mandatory **solo consults on `anthropic/claude-fable-5`**: pre-decomposition per phase, pre-approach and pre-completion per packet, pre-land for P0/high-risk work. Council stays off until the probe row passes; never substitute a weaker model for a failed Fable gate. **AMENDED 2026-07-19 (owner, chat): future questions/acceptations may go to the council consult** — council is the sanctioned fallback when Fable solo caps/fails (record the seats-unproven caveat with each verdict).

**Pause protocol:** if the Fable 5 consult route errors or times out, assume the 5-hour subscription window is exhausted — safely park in-flight work (spine state is durable), write `.spine/handoff.md`, delete/pause loops and monitors, and STOP until the owner resumes with the session prompt. Same response to unresolvable ambiguity, safety/privacy questions, or repeated failure: pause, never improvise past a gate.

### Phase 1 — Milestone 1: foundation contracts and first visible slice

Nine `client/docs/task-board.md` rows, serial (each depends on the prior; `lanes.maxParallel` stays 1). Row 1 runs alone first — the owner reviews its architecture proposal before rows 2–9 are authored:

| Task | Summary | Status | Deps |
|------|---------|--------|------|
| SP-002-bootstrap-architecture | Row 1: architecture proposal instantiating A-001…A-014 + minimal `client/` scaffolding + WSL2 build attempt | **Done 2026-07-18** (landed `5fd1d540`; batch `20260718T120441` recovered from external SIGINT via retry→resume; row 1 stays WIP pending owner ratification) | None |
| SP-003-startup-shutdown-contract | Row 2: startup/shutdown/integration contract — ordered cancellable phases, typed failures, ownership, teardown, integration proof; container admission decision; single-instance CARVED OUT (owner question §5.3) | Authored 2026-07-18 (proposal-review Fable consult applied: single-instance carve-out; engine-review watch item — zero reviews in both prior batches) — **Done 2026-07-18** (landed `eb801810`; batch `20260718T212127` recovered from `worker_orphaned` ×2 via retry; 23/23 tests, headed Windows smoke observed; gate evidence stale post-retry → orchestrator re-run; rows T-2/T-3 filed; row 2 stays WIP pending owner ratification + WSL2 Linux gate) | SP-002 |
| SP-004-async-lifecycle-fault-policy | Row 3: async lifecycle + fault policy — operation ownership (owner/generation/completion task/typed outcome), late-bound phase-4 UI dispatch boundary, generation invalidation, Recoverable/Degraded activation, tested bans | **Done 2026-07-19** (landed `33d5a19a`; batch `20260718T235923` — 3 silent worker deaths root-caused to Windows 32KB command-line limit → worker-runner `@file` patch; GitignoredDirtyWorktree from orchestrator probe → clean+resume; 34/34 tests, headed smoke observed; row 3 stays WIP pending owner ratification + WSL2 Linux gate) | SP-003 |
| SP-005-persistence-migration-contract | Row 4: persistence + migration contract — schema authority, atomic temp+rename write, serialized writer via OperationRegistry, quarantine/Degraded, unknown-member preserve, migration journal, replacement notification, secret seam, STJ decision, teardown-flush activation, WSL2 gate in-packet | **Done 2026-07-19** (landed `0c2c849f`; batch `20260719T010403` — clean run, one GitignoredDirtyWorktree on worker bin/obj → T-5; 51/51 Windows + WSL2 in-packet gate; rows 2/3 WSL2 unit debt closed; row 4 stays WIP pending owner ratification) | SP-004 |
| SP-006-truthful-capability-contract | Row 5: truthful runtime capability contract — typed states + runtime probes, honesty rule (degraded-truthful > fake-available), session-type + atomic-fs demonstrators, WSL2 observed-states gate | **Done 2026-07-19** (landed `66457c87`; batch `20260719T021531` — T-5 recovery ×1; 78/78 Windows + WSL2 with real WSLg honesty proof; orchestrator land consult skipped — session-wide consult cap, see .spine/handoff.md; row 5 stays WIP pending owner ratification; CYCLE PARKED after this land) | SP-005 |

| SP-007-first-visible-slice | Row 6: validate official migration checklist in first visible slice — dashboard window + `demo.status-ticker` demonstrator card, named-observation-per-checklist-item validation doc, Wayland named gate | **Done 2026-07-19** (landed `2d6d846d`; batch `20260719T100547` — relaunched after T-6 false-completion abort of `20260719T093943`; 85/85 Windows + WSL2, headed smoke PASS, WSLg honestly scoped; row 6 stays WIP pending owner ratification + Linux-Wayland gate §5.1 + named manual gates) | SP-006 |

| SP-009-asset-manifest | Row 8: asset/packaged-output manifest — JSON catalogue, two-direction validation, case-exactness, --verify-assets self-check, Debug+Release runs, publish = row-9 gate | **Done 2026-07-19** (landed `48118e29`; batch `20260719T135157` — clean run; 115/115 + 3/3 scratch-verified; orchestrator ran `--verify-assets` directly (exit 0); land consult APPROVED solo Fable; row 8 stays WIP — publish third discharged by row 9) | SP-008 |
| SP-008-verification-harness | Row 7: tiered targeted verification harness — 4 tiers, draw/presentation evidence-class rule, headless admission (evidence-gated), CcpVerify named-check console tool + manifest, seeded-regression self-test, measured budgets | **Done 2026-07-19** (landed `88192528`; batch `20260719T114609` — clean run; 94/94 + 3/3 headless Windows, WSL2 gate in-packet; orchestrator land consult SKIPPED — per-turn consult cap (route healthy, SP-006 precedent); `.gitignore tools/` trap caught pre-land; row 7 stays WIP pending owner ratification; named limits: WSLg lit = settings-restore-driven, self-test Windows-only, tier-4 hook only) | SP-007 |

| SP-010-release-publish-gates | Row 9: Release and publish gates — self-contained single-file per RID named strategy, one version authority, Debug/Release/published matrix Windows+WSL2, row-8 publish third + rows 2/3 WSLg smoke discharged in-packet | Authored 2026-07-19 (pre-authoring Fable consult applied: extraction semantics via current docs, derivation-not-equality version tests, logs/localization verify-absence) | SP-008, SP-009 |

Rows 2–9 (all packets authored):

1. ~~Bootstrap discovery and architecture proposal~~ → SP-002 *(consult checkpoint after: solo Fable 5 reviews the architecture proposal before rows 2–9 are authored; owner reviews asynchronously and may veto — produces `client/` scaffolding + updates `.spine/spine-config.json` testing commands to the real client solution)*
2. ~~Define startup, shutdown, and integration contract~~ → SP-003 *(landed `eb801810`; row stays WIP pending owner ratification + WSL2 Linux re-run)*
3. ~~Establish async lifecycle and fault policy~~ → SP-004 *(landed `33d5a19a`; row stays WIP pending owner ratification + WSL2 Linux re-run)*
4. ~~Define persistence and migration contract~~ → SP-005 *(landed `0c2c849f`; WSL2 gate delivered in-packet; row stays WIP pending owner ratification)*
5. ~~Define truthful runtime capability contract~~ → SP-006 *(landed `66457c87`; WSLg honesty proof delivered; row stays WIP pending owner ratification)*
6. ~~Validate official migration checklist in first visible slice~~ → SP-007 *(landed `2d6d846d`; row stays WIP pending owner ratification + Linux-Wayland gate §5.1 + named manual gates)*
7. ~~Build tiered targeted verification harness~~ → SP-008 *(landed `88192528`; row stays WIP pending owner ratification; `.spine` testing.* now includes the headless project)*
8. ~~Define asset and packaged-output manifest~~ → SP-009 *(landed `48118e29`; row stays WIP — the acceptance's PUBLISH third is row 9's named gate, not discharged)*
7. Build tiered targeted verification harness
8. Define asset and packaged-output manifest
9. ~~Establish Release and publish gates~~ → SP-010 *(authored 2026-07-19)*

Excluded from milestone 1: all BLOCKED rows, all spikes (WebView/DTRH, video handoff/geometry, audio, AI, camera), feature/UI rows, and the Avalonia MCP admission row (owner-only decision). Rationale recorded in board gate history 2026-07-18.

---

## Execution policy

**Operator runbook:** [`docs/adoption/operator-runbook.md`](../docs/adoption/operator-runbook.md) — install, preflight, start/monitor, land loop, gate races, resume/dismiss/complete, dashboard, troubleshooting.

1. **Preflight** before every batch: `spine preflight`.
2. **Land loop:** `spine batch start` → monitor `spine status --diagnose` → `spine gate approve` → `spine integrate` → `spine batch complete`.
3. **Never** hand-edit `.spine/batch-state.json`.
4. **Windows PATH:** `spine` is not on bash PATH by default — `export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin"` first, or invoke the `.cmd` shim.
5. **Stub first:** run `SPINE_WORKER_STUB=1 spine batch start <id>` once per new packet shape before real workers.
6. **Testing commands:** `.spine/spine-config.json` `testing.*` carry the real client commands since SP-002 land (2026-07-18): `dotnet build client/CcpClient.sln -c Debug --nologo` / `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`. Each packet's `## Contract` `testCommand` may still narrow scope. NOTE: the spine gate-evidence executable allowlist (`evidence-command.mjs`) is node-only upstream; `dotnet` was added via local node_modules patch (does NOT survive pi-spine reinstall — re-apply with the fsync patch; see port-lessons).
7. **Board reconciliation:** every task updates its `client/docs/task-board.md` row before `.DONE`; the board wins over spine state on conflict.
