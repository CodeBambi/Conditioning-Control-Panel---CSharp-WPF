# Conditioning-Control-Panel — Context

**Last Updated:** 2026-07-18
**Status:** Active
**Next Task ID:** SP-002

---

## Current State

Greenfield Avalonia port (second attempt), zero product code under `client/` yet. Execution engine: pi-spine (owner-decided 2026-07-18, replacing `@mjasnikovs/pi-task` — see `client/docs/task-board.md` gate history). Product queue authority is `client/docs/task-board.md`; this file tracks spine execution phases only. Workers obey `docs/constitution.md`.

### Phase 0 — Engine pilot and gates

| Task | Summary | Status | Deps |
|------|---------|--------|------|
| SP-001-avalonia-template-pilot | Throwaway Avalonia 12 template spike proving the spine pipeline end-to-end (real dotnet contract, review, gate, integrate) | Pending | None |

**Gates before Phase 1 (owner-held, not packets):**

1. SP-001 landed and owner judges the pilot against `client/docs/port-workflow.md` §Pilot — this flips the board row "Pilot pinned spine batch workflow".
2. Board row "Probe bpx-consult council and task integration" resolved — council seats are unproven (kimi routes not engaging, recorded 2026-07-18); until then packets use **solo** consult gates.
3. Owner approves the Phase 1 decomposition below (which board rows, order, exclusions).

### Phase 1 — Milestone 1: foundation contracts and first visible slice (packets not yet authored)

Nine `client/docs/task-board.md` rows, serial (each depends on the prior; `lanes.maxParallel` stays 1). Row 1 runs alone first — the owner reviews its architecture proposal before rows 2–9 are authored:

1. Bootstrap discovery and architecture proposal *(owner checkpoint after — produces `client/` scaffolding + updates `.spine/spine-config.json` testing commands to the real client solution)*
2. Define startup, shutdown, and integration contract
3. Establish async lifecycle and fault policy
4. Define persistence and migration contract
5. Define truthful runtime capability contract
6. Validate official migration checklist in first visible slice
7. Build tiered targeted verification harness
8. Define asset and packaged-output manifest
9. Establish Release and publish gates

Excluded from milestone 1: all BLOCKED rows, all spikes (WebView/DTRH, video handoff/geometry, audio, AI, camera), feature/UI rows, and the Avalonia MCP admission row (owner-only decision). Rationale recorded in board gate history 2026-07-18.

---

## Execution policy

**Operator runbook:** [`docs/adoption/operator-runbook.md`](../docs/adoption/operator-runbook.md) — install, preflight, start/monitor, land loop, gate races, resume/dismiss/complete, dashboard, troubleshooting.

1. **Preflight** before every batch: `spine preflight`.
2. **Land loop:** `spine batch start` → monitor `spine status --diagnose` → `spine gate approve` → `spine integrate` → `spine batch complete`.
3. **Never** hand-edit `.spine/batch-state.json`.
4. **Windows PATH:** `spine` is not on bash PATH by default — `export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin"` first, or invoke the `.cmd` shim.
5. **Stub first:** run `SPINE_WORKER_STUB=1 spine batch start <id>` once per new packet shape before real workers.
6. **Testing commands:** `.spine/spine-config.json` `testing.*` are `git diff --check` placeholders until row 1 creates the client solution; each packet's `## Contract` `testCommand` carries the real scoped dotnet command.
7. **Board reconciliation:** every task updates its `client/docs/task-board.md` row before `.DONE`; the board wins over spine state on conflict.
