# Status: SP-006 — truthful runtime capability contract

**Overall:** ◐ In Progress

**Current Step:** 1

## Steps

### Step 1: Pre-approach consult and contract draft
**Status:** ◐ In Progress

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] First-attempt capability evidence digest in record.md (three lying patterns, outcomes only)
- [x] `client/docs/runtime-capability-contract.md` written (state model, probe rule, honesty rule, degradation semantics, row-3/row-5 boundary, fallback policy, session-type reporting)

### Step 2: Capability registry and demonstrator probes
**Status:** ⬜ Not Started

- [ ] Registry + probe execution via OperationRegistry; typed states with reasons; exceptions → Faulted
- [ ] Demonstrator 1: session-type probe (Windows/X11/Wayland; XWayland distinction recorded)
- [ ] Demonstrator 2: atomic filesystem probe (real I/O; degrades truthfully on DrvFs)
- [ ] Unit tests: all states reachable; probe-throw → Faulted; unregistered → Unavailable; registration alone never Available

### Step 3: Wiring, WSL2 honesty run, integration proof
**Status:** ⬜ Not Started

- [ ] Probes run in named startup phase as owned operations; window surfaces states (integration proof)
- [ ] WSL2 run: testCommand + ACTUAL observed demonstrator states recorded (degraded = honesty proof)
- [ ] Headed Windows smoke: UIA-observed truthful states; exit 0; recorded
- [ ] Composition-root path populates states via real probes (no doubles on that path)

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md written (incl. engine-review presence/absence note)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 5 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
