# Status: SP-006 — truthful runtime capability contract

**Overall:** ◐ In Progress

**Current Step:** 5

## Steps

### Step 1: Pre-approach consult and contract draft
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] First-attempt capability evidence digest in record.md (three lying patterns, outcomes only)
- [x] `client/docs/runtime-capability-contract.md` written (state model, probe rule, honesty rule, degradation semantics, row-3/row-5 boundary, fallback policy, session-type reporting)

### Step 2: Capability registry and demonstrator probes
**Status:** ✅ Complete

- [x] Registry + probe execution via OperationRegistry; typed states with reasons; exceptions → Faulted
- [x] Demonstrator 1: session-type probe (Windows/X11/Wayland; XWayland distinction recorded)
- [x] Demonstrator 2: atomic filesystem probe (real I/O; degrades truthfully on DrvFs)
- [x] Unit tests: all states reachable; probe-throw → Faulted; unregistered → Unavailable; registration alone never Available

### Step 3: Wiring, WSL2 honesty run, integration proof
**Status:** ✅ Complete

- [x] Probes run in named startup phase as owned operations; window surfaces states (integration proof) — wired in Step 2 commit (CapabilityProbes phase + MainWindow surface)
- [x] WSL2 run: testCommand + ACTUAL observed demonstrator states recorded (degraded = honesty proof)
- [x] Headed Windows smoke: UIA-observed truthful states; exit 0; recorded
- [x] Composition-root path populates states via real probes (no doubles on that path)

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ✅ Complete

- [x] record.md written (incl. engine-review presence/absence note)
- [x] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [x] Board row 5 → `WIP` with evidence (not `DONE`)
- [x] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ◐ In Progress

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
